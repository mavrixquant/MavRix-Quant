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

// This namespace holds Strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies.MavRix
{
    public class MavRixATREMA : Strategy
    {
        private EMA emaFast;
        private EMA emaSlow;
        private ATR atr;

        #region User Inputs

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ATR Length", GroupName = "1. Indicators", Order = 1)]
        public int ATRLength { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Fast Length", GroupName = "1. Indicators", Order = 2)]
        public int EMAFastLength { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Slow Length", GroupName = "1. Indicators", Order = 3)]
        public int EMASlowLength { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "SL ATR Multiplier", GroupName = "2. Risk", Order = 1)]
        public double SLATRMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Reward:Risk (RR)", GroupName = "2. Risk", Order = 2)]
        public double RewardToRisk { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Fixed $ Risk (unchecked = % of account)", GroupName = "2. Risk", Order = 3)]
        public bool UseFixedRiskAmount { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Fixed Risk Amount ($)", GroupName = "2. Risk", Order = 4)]
        public double FixedRiskAmount { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Risk % of Account", GroupName = "2. Risk", Order = 5)]
        public double RiskPercentOfAccount { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Contracts (safety cap)", GroupName = "2. Risk", Order = 6)]
        public int MaxContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start Time (HHmmss)", GroupName = "3. Time Window", Order = 1)]
        public int StartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session End Time (HHmmss)", GroupName = "3. Time Window", Order = 2)]
        public int EndTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Daily Loss Limit", GroupName = "4. Daily Loss Limit", Order = 1)]
        public bool EnableDailyLossLimit { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Daily Loss Limit (net R, positive number)", GroupName = "4. Daily Loss Limit", Order = 2)]
        public double DailyLossLimitRR { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exit & Reverse On Opposite Signal", GroupName = "5. Exit Rules", Order = 1)]
        public bool ExitAndReverseOnOppositeSignal { get; set; }

        #endregion

        // Daily loss limit tracking
        private double dailyNetRR = 0;
        private DateTime currentTradingDay = DateTime.MinValue;
        private bool dailyLimitHit = false;

        // Per-trade risk tracking (single position at a time)
        private double currentTradeRiskDollars = 0;
        private bool wasInPosition = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "EMA Cross entries, ATR-based SL, RR-based TP, time window filter, dynamic position sizing.";
                Name = "MavRixATREMA";
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
                BarsRequiredToTrade = 30;
                IsInstantiatedOnEachOptimizationIteration = true;

                // Default input values
                ATRLength = 14;
                EMAFastLength = 10;
                EMASlowLength = 23;
                SLATRMultiplier = 1.5;
                RewardToRisk = 1.0;
                UseFixedRiskAmount = false;
                FixedRiskAmount = 500;
                RiskPercentOfAccount = 1.0;
                MaxContracts = 50;
                StartTime = 122000;
                EndTime = 223000;
                EnableDailyLossLimit = true;
                DailyLossLimitRR = 3.0;
                ExitAndReverseOnOppositeSignal = true;
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.DataLoaded)
            {
                emaFast = EMA(EMAFastLength);
                emaSlow = EMA(EMASlowLength);
                atr = ATR(ATRLength);

                emaFast.Plots[0].Brush = System.Windows.Media.Brushes.DodgerBlue;
                emaSlow.Plots[0].Brush = System.Windows.Media.Brushes.Orange;

                AddChartIndicator(emaFast);
                AddChartIndicator(emaSlow);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < BarsRequiredToTrade)
                return;

            CheckForNewTradingDay();

            double atrValue = atr[0];
            if (atrValue <= 0)
                return;

            bool crossUp = CrossAbove(emaFast, emaSlow, 1);
            bool crossDown = CrossBelow(emaFast, emaSlow, 1);

            if (!crossUp && !crossDown)
                return;

            bool inWindow = IsInsideTimeWindow();
            bool dailyLimitReached = EnableDailyLossLimit && dailyNetRR <= -Math.Abs(DailyLossLimitRR);

            if (dailyLimitReached && !dailyLimitHit)
            {
                dailyLimitHit = true;
                Print(string.Format("{0} :: Daily loss limit reached ({1:0.00}R). No new entries until next trading day.", Time[0], dailyNetRR));
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                // Flat: only ever open a new trade inside the time window and under the daily loss limit
                if (!inWindow || dailyLimitReached)
                    return;

                if (crossUp)
                    EnterLongSetup(atrValue);
                else if (crossDown)
                    EnterShortSetup(atrValue);
            }
            else if (ExitAndReverseOnOppositeSignal)
            {
                // Already in a trade: an opposite-direction signal closes the current
                // position immediately (in addition to the existing SL/TP exits), and
                // opens the new trade in its place -- subject to the same window/loss-limit rules.
                if (Position.MarketPosition == MarketPosition.Long && crossDown)
                {
                    ExitLong("ExitOnReverse", "Long");

                    if (inWindow && !dailyLimitReached)
                        EnterShortSetup(atrValue);
                }
                else if (Position.MarketPosition == MarketPosition.Short && crossUp)
                {
                    ExitShort("ExitOnReverse", "Short");

                    if (inWindow && !dailyLimitReached)
                        EnterLongSetup(atrValue);
                }
            }
        }

        private void CheckForNewTradingDay()
        {
            DateTime barDate = Time[0].Date;
            if (barDate != currentTradingDay)
            {
                currentTradingDay = barDate;
                dailyNetRR = 0;
                dailyLimitHit = false;
            }
        }

        /// <summary>
        /// Called whenever the strategy's position changes. Used to detect when a trade
        /// has fully closed so we can convert its realized P&amp;L into an R multiple and
        /// add it to the running daily net RR total.
        /// </summary>
        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            if (marketPosition == MarketPosition.Flat)
            {
                if (wasInPosition)
                {
                    wasInPosition = false;
                    RecordTradeResult();
                }
            }
            else
            {
                wasInPosition = true;
            }
        }

        private void RecordTradeResult()
        {
            if (SystemPerformance == null || SystemPerformance.AllTrades == null || SystemPerformance.AllTrades.Count == 0)
                return;

            Trade lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
            double profit = lastTrade.ProfitCurrency;

            if (currentTradeRiskDollars > 0)
            {
                double rMultiple = profit / currentTradeRiskDollars;
                dailyNetRR += rMultiple;

                Print(string.Format("{0} :: Trade closed, P/L=${1:0.00}, Risk=${2:0.00}, R={3:0.00}, Daily Net RR={4:0.00}",
                    Time[0], profit, currentTradeRiskDollars, rMultiple, dailyNetRR));
            }

            currentTradeRiskDollars = 0;
        }

        private bool IsInsideTimeWindow()
        {
            int barTime = ToTime(Time[0]);

            if (StartTime <= EndTime)
                return barTime >= StartTime && barTime <= EndTime;

            // Handles a window that spans midnight, e.g. 220000 to 060000
            return barTime >= StartTime || barTime <= EndTime;
        }

        private void EnterLongSetup(double atrValue)
        {
            double closePrice = Close[0];
            double stopDistance = RoundToTickSize(atrValue * SLATRMultiplier);

            if (stopDistance <= 0)
                return;

            double slPrice = RoundToTickSize(closePrice - stopDistance);
            double tpPrice = RoundToTickSize(closePrice + stopDistance * RewardToRisk);

            int quantity = CalculateQuantity(stopDistance);
            if (quantity <= 0)
                return;

            currentTradeRiskDollars = quantity * stopDistance * Instrument.MasterInstrument.PointValue;

            SetStopLoss("Long", CalculationMode.Price, slPrice, false);
            SetProfitTarget("Long", CalculationMode.Price, tpPrice);
            EnterLong(quantity, "Long");
        }

        private void EnterShortSetup(double atrValue)
        {
            double closePrice = Close[0];
            double stopDistance = RoundToTickSize(atrValue * SLATRMultiplier);

            if (stopDistance <= 0)
                return;

            double slPrice = RoundToTickSize(closePrice + stopDistance);
            double tpPrice = RoundToTickSize(closePrice - stopDistance * RewardToRisk);

            int quantity = CalculateQuantity(stopDistance);
            if (quantity <= 0)
                return;

            currentTradeRiskDollars = quantity * stopDistance * Instrument.MasterInstrument.PointValue;

            SetStopLoss("Short", CalculationMode.Price, slPrice, false);
            SetProfitTarget("Short", CalculationMode.Price, tpPrice);
            EnterShort(quantity, "Short");
        }

        /// <summary>
        /// Calculates contract/position size from risk amount and stop distance, rounded down to the nearest whole contract (natural number, min 1).
        /// </summary>
        private int CalculateQuantity(double stopDistance)
        {
            double pointValue = Instrument.MasterInstrument.PointValue;
            if (pointValue <= 0 || stopDistance <= 0)
                return 1;

            double riskAmount = GetRiskAmount();
            if (riskAmount <= 0)
                return 1;

            double rawQuantity = riskAmount / (stopDistance * pointValue);

            int quantity = (int)Math.Floor(rawQuantity);

            if (quantity < 1)
                quantity = 1;

            if (quantity > MaxContracts)
                quantity = MaxContracts;

            return quantity;
        }

        private double GetRiskAmount()
        {
            if (UseFixedRiskAmount)
                return FixedRiskAmount;

            double accountValue = 0;

            try
            {
                if (Account != null)
                    accountValue = Account.Get(AccountItem.CashValue, Currency.UsDollar);
            }
            catch
            {
                accountValue = 0;
            }

            if (accountValue <= 0)
                return FixedRiskAmount; // fallback if account value unavailable (e.g. some backtest modes)

            return accountValue * (RiskPercentOfAccount / 100.0);
        }

        private double RoundToTickSize(double price)
        {
            double tickSize = TickSize;
            if (tickSize <= 0)
                return price;

            return Instrument.MasterInstrument.RoundToTickSize(price);
        }
    }
}
