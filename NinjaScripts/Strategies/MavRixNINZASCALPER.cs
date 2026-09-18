#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

// ===================================================================================================
// MavRixNinzaScalper
// ---------------------------------------------------------------------------------------------------
// Designed to run on a chart already set to "Ninza Renko" bars (Brick Size 64 / Threshold 16).
// NinjaScript strategies operate on whatever OHLC bars the chart provides, so the Renko brick
// construction itself is NOT reproduced here - it must be the chart's bar type. This script only
// consumes Open/High/Low/Close of those bricks.
//
// TREND BIAS
//   EMA(EMAFastPeriod) vs EMA(EMASlowPeriod). Fast > Slow => Bull bias (Buys only).
//   Fast < Slow => Bear bias (Sells only). Fast == Slow => flat/no trade.
//
// PATTERN / ENTRY LOGIC (BUY SIDE - SELL SIDE IS THE MIRROR IMAGE)
//   1. Bull trend must be active.
//   2. A run of exactly StartCandle consecutive BULL-closed candles forms immediately after a
//      color flip (i.e. Bear -> Bull1 -> Bull2 when StartCandle = 2). The candle where the run
//      reaches StartCandle length is the "anchor" candle.
//   3. From the next candle onward, every new candle is compared against the PREVIOUS candle's
//      Open:
//         if Low[current] < Open[previous]  (and, for safety, Close[current] > Open[previous],
//         i.e. price wicked below the reference open but reclaimed it) -> Enter Long.
//         Stop Loss = Open[previous] (the reference level that was reclaimed).
//      NOTE: the "Close back above" reclaim clause is an added safety condition - see the message
//      accompanying this file for why, and how to remove it if you want the literal spec instead.
//   4. If no trigger fires, the reference simply becomes the next candle's Open and the check
//      slides forward one candle at a time until CandlePosition > EndCandle, at which point the
//      setup is abandoned and the strategy waits for a brand new StartCandle-length run.
//
// TIME WINDOW
//   Entries are only allowed when the bar time (HHmmss) is between StartTime and EndTime.
//   Existing open positions are still managed (stop/target) outside the window.
//
// DAILY RISK MANAGEMENT (measured in realized R, i.e. multiples of each trade's own risk)
//   DailyLossLimitR   - trading halts for the rest of the session once net R <= -DailyLossLimitR.
//   DailyProfitLimitR - trading halts for the rest of the session once net R >= DailyProfitLimitR.
//   R for each closed trade = (exitPrice - entryPrice) / riskPerUnit, signed for direction, so a
//   trade that exits at 2x its stop distance in profit = +2R, a full stop-out = -1R, etc.
// ===================================================================================================

namespace NinjaTrader.NinjaScript.Strategies.MavRix
{
	public class MavRixNinzaScalper : Strategy
	{
		#region Inputs

		[NinjaScriptProperty]
		[Display(Name = "EMA Fast Period", GroupName = "01 - Trend", Order = 1)]
		public int EMAFastPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "EMA Slow Period", GroupName = "01 - Trend", Order = 2)]
		public int EMASlowPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Start Candle", GroupName = "02 - Pattern", Order = 1)]
		public int StartCandle { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "End Candle", GroupName = "02 - Pattern", Order = 2)]
		public int EndCandle { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require Wick Reclaim (Close back over ref Open)", GroupName = "02 - Pattern", Order = 3)]
		public bool RequireReclaim { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Per-Trade Target (R multiple)", GroupName = "03 - Risk", Order = 1)]
		public double TargetRR { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Daily Loss Limit (net R, positive number)", GroupName = "03 - Risk", Order = 2)]
		public double DailyLossLimitR { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Daily Profit Limit (net R)", GroupName = "03 - Risk", Order = 3)]
		public double DailyProfitLimitR { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Trade Quantity", GroupName = "03 - Risk", Order = 4)]
		public int TradeQuantity { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Start Time (HHmmss)", GroupName = "04 - Session", Order = 1)]
		public int StartTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "End Time (HHmmss)", GroupName = "04 - Session", Order = 2)]
		public int EndTime { get; set; }

		#endregion

		#region Private state

		private EMA emaFast;
		private EMA emaSlow;

		private enum TrendState { None, Bull, Bear }
		private enum RunColor { None, Bull, Bear }

		private TrendState trend = TrendState.None;

		private RunColor runColor = RunColor.None;
		private int runLength = 0;

		private bool buyPatternActive = false;
		private int buyPositionCounter = 0;

		private bool sellPatternActive = false;
		private int sellPositionCounter = 0;

		// Active trade bookkeeping (for R computation)
		private double entryPriceStored = 0.0;
		private double stopPriceStored = 0.0;
		private double riskPerUnit = 0.0;
		private int tradeDirection = 0; // +1 long, -1 short

		// Daily risk tracking
		private DateTime currentSessionDate = DateTime.MinValue;
		private double dailyNetR = 0.0;
		private bool tradingHaltedForDay = false;

		private int entrySignalCounter = 0;

		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "MavRix Ninza Scalper - Renko trend-run pullback-reclaim strategy";
				Name = "MavRixNINZASCALPER";
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds = 30;
				IsFillLimitOnTouch = false;
				MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution = OrderFillResolution.Standard;
				Slippage = 0;
				StartBehavior = StartBehavior.WaitUntilFlat;
				TimeInForce = TimeInForce.Gtc;
				TraceOrders = false;
				RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling = StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade = 20;
				IsInstantiatedOnEachOptimizationIteration = true;

				// ---- Defaults matching the spec ----
				EMAFastPeriod = 10;
				EMASlowPeriod = 23;
				StartCandle = 2;
				EndCandle = 5;
				RequireReclaim = true;
				TargetRR = 2.0;
				DailyLossLimitR = 3.0;
				DailyProfitLimitR = 6.0;
				TradeQuantity = 1;
				StartTime = 122000;
				EndTime = 223000;
			}
			else if (State == State.Configure)
			{
				// Primary series only - assumes chart bars are already Ninza Renko 64/16
			}
			else if (State == State.DataLoaded)
			{
				emaFast = EMA(EMAFastPeriod);
				emaSlow = EMA(EMASlowPeriod);
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0)
				return;

			if (CurrentBar < Math.Max(EMASlowPeriod, StartCandle) + 2)
				return;

			ResetDailyStateIfNewSession();
			UpdateTrend();

			bool inWindow = IsInTimeWindow(Time[0]);
			bool canEnter = !tradingHaltedForDay && inWindow && Position.MarketPosition == MarketPosition.Flat;

			// -----------------------------------------------------------------
			// 1) Evaluate already-armed patterns FIRST (uses this bar vs the
			//    immediately preceding bar's Open - the sliding reference).
			// -----------------------------------------------------------------
			EvaluateBuyPattern(canEnter);
			EvaluateSellPattern(canEnter);

			// -----------------------------------------------------------------
			// 2) Update the consecutive same-color run counter using THIS bar.
			// -----------------------------------------------------------------
			UpdateRun();

			// -----------------------------------------------------------------
			// 3) Arm a brand new pattern if a fresh StartCandle-length run just
			//    completed and no pattern of that direction is already active.
			// -----------------------------------------------------------------
			if (trend == TrendState.Bull && !buyPatternActive && runColor == RunColor.Bull && runLength == StartCandle)
			{
				buyPatternActive = true;
				buyPositionCounter = StartCandle;
			}

			if (trend == TrendState.Bear && !sellPatternActive && runColor == RunColor.Bear && runLength == StartCandle)
			{
				sellPatternActive = true;
				sellPositionCounter = StartCandle;
			}

			// If trend flips, drop whatever pattern belongs to the now-invalid direction
			if (trend != TrendState.Bull)
			{
				buyPatternActive = false;
			}
			if (trend != TrendState.Bear)
			{
				sellPatternActive = false;
			}
		}

		#region Trend

		private void UpdateTrend()
		{
			double fast = emaFast[0];
			double slow = emaSlow[0];

			if (fast > slow)
				trend = TrendState.Bull;
			else if (fast < slow)
				trend = TrendState.Bear;
			else
				trend = TrendState.None;
		}

		#endregion

		#region Run tracking

		private void UpdateRun()
		{
			bool isBull = Close[0] > Open[0];
			bool isBear = Close[0] < Open[0];

			if (isBull)
			{
				if (runColor == RunColor.Bull)
					runLength++;
				else
				{
					runColor = RunColor.Bull;
					runLength = 1;
				}
			}
			else if (isBear)
			{
				if (runColor == RunColor.Bear)
					runLength++;
				else
				{
					runColor = RunColor.Bear;
					runLength = 1;
				}
			}
			// Doji (Close == Open): leave run state unchanged
		}

		#endregion

		#region Pattern evaluation / entries

		private void EvaluateBuyPattern(bool canEnter)
		{
			if (!buyPatternActive)
				return;

			buyPositionCounter++;

			if (buyPositionCounter > EndCandle)
			{
				buyPatternActive = false; // window expired, no trigger
				return;
			}

			double referenceOpen = Open[1]; // previous candle's Open = sliding reference

			bool wickBreak = Low[0] < referenceOpen;
			bool reclaimOk = !RequireReclaim || Close[0] > referenceOpen;

			if (trend == TrendState.Bull && canEnter && wickBreak && reclaimOk)
			{
				double entry = Close[0];
				double stop = referenceOpen;

				if (entry - stop > TickSize) // must be a valid positive risk
				{
					DoLongEntry(entry, stop);
					buyPatternActive = false; // consumed
				}
			}
		}

		private void EvaluateSellPattern(bool canEnter)
		{
			if (!sellPatternActive)
				return;

			sellPositionCounter++;

			if (sellPositionCounter > EndCandle)
			{
				sellPatternActive = false; // window expired, no trigger
				return;
			}

			double referenceOpen = Open[1]; // previous candle's Open = sliding reference

			bool wickBreak = High[0] > referenceOpen;
			bool reclaimOk = !RequireReclaim || Close[0] < referenceOpen;

			if (trend == TrendState.Bear && canEnter && wickBreak && reclaimOk)
			{
				double entry = Close[0];
				double stop = referenceOpen;

				if (stop - entry > TickSize) // must be a valid positive risk
				{
					DoShortEntry(entry, stop);
					sellPatternActive = false; // consumed
				}
			}
		}

		private void DoLongEntry(double approxEntry, double stop)
		{
			entrySignalCounter++;
			string signal = "MRXBuy" + entrySignalCounter;
		
			double risk = approxEntry - stop;
			double target = approxEntry + risk * TargetRR;
		
			// Changed from Market to StopMarket using the Close price (approxEntry)
			EnterLongStopMarket(TradeQuantity, approxEntry, signal);
			
			SetStopLoss(signal, CalculationMode.Price, stop, false);
			SetProfitTarget(signal, CalculationMode.Price, target);
		
			tradeDirection = 1;
			stopPriceStored = stop;
			riskPerUnit = risk;
			entryPriceStored = approxEntry; // refined with actual fill in OnExecutionUpdate
		}
		
		private void DoShortEntry(double approxEntry, double stop)
		{
			entrySignalCounter++;
			string signal = "MRXSell" + entrySignalCounter;
		
			double risk = stop - approxEntry;
			double target = approxEntry - risk * TargetRR;
		
			// Changed from Market to StopMarket using the Close price (approxEntry)
			EnterShortStopMarket(TradeQuantity, approxEntry, signal);
			
			SetStopLoss(signal, CalculationMode.Price, stop, false);
			SetProfitTarget(signal, CalculationMode.Price, target);
		
			tradeDirection = -1;
			stopPriceStored = stop;
			riskPerUnit = risk;
			entryPriceStored = approxEntry; // refined with actual fill in OnExecutionUpdate
		}

		#endregion

		#region Time window

		private bool IsInTimeWindow(DateTime barTime)
		{
			int t = ToTime(barTime);

			if (StartTime <= EndTime)
				return t >= StartTime && t <= EndTime;

			// Handles a window that crosses midnight, just in case
			return t >= StartTime || t <= EndTime;
		}

		#endregion

		#region Daily risk management

		private void ResetDailyStateIfNewSession()
		{
			DateTime barDate = Time[0].Date;

			if (barDate != currentSessionDate)
			{
				currentSessionDate = barDate;
				dailyNetR = 0.0;
				tradingHaltedForDay = false;
			}
		}

		private void UpdateDailyR(double exitPrice)
		{
			if (riskPerUnit <= 0)
				return;

			double r = tradeDirection * (exitPrice - entryPriceStored) / riskPerUnit;
			dailyNetR += r;

			Print(string.Format("{0} Trade closed. Trade R = {1:0.00}, Daily Net R = {2:0.00}",
				Time[0], r, dailyNetR));

			if (dailyNetR <= -Math.Abs(DailyLossLimitR))
			{
				tradingHaltedForDay = true;
				Print(string.Format("{0} Daily LOSS limit reached ({1:0.00}R). Trading halted for the session.",
					Time[0], dailyNetR));
			}
			else if (dailyNetR >= Math.Abs(DailyProfitLimitR))
			{
				tradingHaltedForDay = true;
				Print(string.Format("{0} Daily PROFIT limit reached ({1:0.00}R). Trading halted for the session.",
					Time[0], dailyNetR));
			}
		}

		#endregion

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price,
			int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (execution == null || execution.Order == null)
				return;

			string name = execution.Order.Name;

			// Capture the actual entry fill price for accurate R math
			if (name.StartsWith("MRXBuy") || name.StartsWith("MRXSell"))
			{
				entryPriceStored = price;
				riskPerUnit = Math.Abs(entryPriceStored - stopPriceStored);
				return;
			}

			// Any exit (stop loss, profit target, or session-close flatten) closes the trade
			bool isExit = name == "Stop loss" || name == "Profit target" ||
				name.IndexOf("Stop loss", StringComparison.OrdinalIgnoreCase) >= 0 ||
				name.IndexOf("Profit target", StringComparison.OrdinalIgnoreCase) >= 0;

			if (isExit && Position.MarketPosition == MarketPosition.Flat)
			{
				UpdateDailyR(price);
			}
		}
	}
}