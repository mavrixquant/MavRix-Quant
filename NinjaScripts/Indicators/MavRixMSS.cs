#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// This namespace holds indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
	/// <summary>
	/// Market Structure Shift (MSS)
	///
	/// Logic:
	///  1. Confirmed swing highs / lows are found using a fractal lookback (SwingStrength bars
	///     on each side of the candidate pivot).
	///  2. The most recent confirmed swing high and swing low are tracked as the "active"
	///     structure levels until they are broken.
	///  3. A break of the active swing HIGH while bias is Bearish/Neutral -> Bullish MSS.
	///     A break of the active swing HIGH while bias is already Bullish -> plain BOS (continuation).
	///     A break of the active swing LOW  while bias is Bullish/Neutral -> Bearish MSS.
	///     A break of the active swing LOW  while bias is already Bearish -> plain BOS (continuation).
	///  4. MSS events redraw the internal bias, so the next break is judged against the new bias.
	/// </summary>
	public class MavRixMSS : Indicator
	{
		private double swingHighPrice = double.MinValue;
		private double swingLowPrice = double.MaxValue;
		private int swingHighBar = -1;   // absolute CurrentBar index at which the pivot was confirmed
		private int swingLowBar = -1;
		private bool swingHighBroken = true;
		private bool swingLowBroken = true;

		// 0 = neutral, 1 = bullish, -1 = bearish
		private int bias = 0;

		private Series<double> pivotHighSeries;
		private Series<double> pivotLowSeries;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= @"Detects confirmed swing highs/lows and flags a Market Structure Shift (MSS) when price closes through a swing point against the prevailing bias. Same-direction breaks are labeled BOS (Break of Structure) for context.";
				Name						= "MavRixMSS";
				Calculate					= Calculate.OnBarClose;
				IsOverlay					= true;
				DisplayInDataBox			= false;
				DrawOnPricePanel			= true;
				DrawHorizontalGridLines		= true;
				DrawVerticalGridLines		= true;
				PaintPriceMarkers			= true;
				ScaleJustification			= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive	= true;

				SwingStrength		= 5;
				ShowSwingPoints	= true;
				ShowBOS			= true;
				ExtendLevelLines	= false;
				EnableAlerts		= false;
				BullishBrush		= Brushes.DodgerBlue;
				BearishBrush		= Brushes.OrangeRed;
				BosBrush			= Brushes.Gray;
				SwingDotBrush		= Brushes.DarkGray;
			}
			else if (State == State.Configure)
			{
			}
			else if (State == State.DataLoaded)
			{
				pivotHighSeries = new Series<double>(this);
				pivotLowSeries  = new Series<double>(this);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 2 * SwingStrength + 1)
				return;

			// ---------------------------------------------------------------
			// 1. Pivot detection (fractal). The candidate bar sits SwingStrength
			//    bars back so it has SwingStrength bars of confirmation on both sides.
			// ---------------------------------------------------------------
			bool isPivotHigh = true;
			bool isPivotLow  = true;

			double candidateHigh = High[SwingStrength];
			double candidateLow  = Low[SwingStrength];

			for (int i = 1; i <= SwingStrength; i++)
			{
				if (High[SwingStrength - i] > candidateHigh || High[SwingStrength + i] > candidateHigh)
					isPivotHigh = false;

				if (Low[SwingStrength - i] < candidateLow || Low[SwingStrength + i] < candidateLow)
					isPivotLow = false;

				if (!isPivotHigh && !isPivotLow)
					break;
			}

			int pivotBarIndex = CurrentBar - SwingStrength;

			if (isPivotHigh)
			{
				swingHighPrice  = candidateHigh;
				swingHighBar    = pivotBarIndex;
				swingHighBroken = false;

				if (ShowSwingPoints)
					Draw.Dot(this, "SwingHigh" + pivotBarIndex, false, SwingStrength, candidateHigh + 2 * TickSize, SwingDotBrush);
			}

			if (isPivotLow)
			{
				swingLowPrice  = candidateLow;
				swingLowBar    = pivotBarIndex;
				swingLowBroken = false;

				if (ShowSwingPoints)
					Draw.Dot(this, "SwingLow" + pivotBarIndex, false, SwingStrength, candidateLow - 2 * TickSize, SwingDotBrush);
			}

			// ---------------------------------------------------------------
			// 2. Break checks against the currently active (unbroken) swing points.
			// ---------------------------------------------------------------
			if (!swingHighBroken && swingHighBar >= 0 && Close[0] > swingHighPrice)
			{
				swingHighBroken = true;
				bool isMSS = bias <= 0;      // was bearish or neutral -> genuine shift
				bias = 1;

				HandleBreak(true, isMSS, swingHighBar, swingHighPrice);
			}

			if (!swingLowBroken && swingLowBar >= 0 && Close[0] < swingLowPrice)
			{
				swingLowBroken = true;
				bool isMSS = bias >= 0;      // was bullish or neutral -> genuine shift
				bias = -1;

				HandleBreak(false, isMSS, swingLowBar, swingLowPrice);
			}
		}

		/// <summary>
		/// Draws the level line, arrow and label for a broken swing point, and optionally fires an alert.
		/// </summary>
		private void HandleBreak(bool bullish, bool isMSS, int levelBar, double levelPrice)
		{
			if (!isMSS && !ShowBOS)
				return;

			int barsAgoOfLevel = CurrentBar - levelBar;
			if (barsAgoOfLevel < 0)
				barsAgoOfLevel = 0;

			Brush lineBrush = isMSS ? (bullish ? BullishBrush : BearishBrush) : BosBrush;
			string tagBase  = (isMSS ? "MSS" : "BOS") + (bullish ? "Up" : "Dn") + CurrentBar;

			int lineEndBarsAgo = ExtendLevelLines ? 0 : 0; // line always ends at the breaking bar (barsAgo 0)

			Draw.Line(this, tagBase + "Line", false, barsAgoOfLevel, levelPrice, lineEndBarsAgo, levelPrice,
				lineBrush, isMSS ? DashStyleHelper.Solid : DashStyleHelper.Dash, isMSS ? 2 : 1);

			if (bullish)
				Draw.ArrowUp(this, tagBase + "Arrow", false, 0, Low[0] - 3 * TickSize, lineBrush);
			else
				Draw.ArrowDown(this, tagBase + "Arrow", false, 0, High[0] + 3 * TickSize, lineBrush);

			if (EnableAlerts)
			{
				string alertName = (isMSS ? "Market Structure Shift " : "Break of Structure ") + (bullish ? "Bullish" : "Bearish");
				Alert(tagBase, Priority.Medium, alertName,
					NinjaTrader.Core.Globals.InstallDir + "\\sounds\\Alert1.wav", 10, Brushes.Black, lineBrush);
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Swing Strength (bars each side)", Order = 1, GroupName = "Parameters")]
		public int SwingStrength { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Swing Points", Order = 2, GroupName = "Parameters")]
		public bool ShowSwingPoints { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show BOS (same-direction breaks)", Order = 3, GroupName = "Parameters")]
		public bool ShowBOS { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Extend Level Lines", Order = 4, GroupName = "Parameters")]
		public bool ExtendLevelLines { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Alerts", Order = 5, GroupName = "Parameters")]
		public bool EnableAlerts { get; set; }

		[XmlIgnore]
		[Display(Name = "Bullish MSS Color", Order = 6, GroupName = "Visuals")]
		public Brush BullishBrush { get; set; }

		[Browsable(false)]
		public string BullishBrushSerialize
		{
			get { return Serialize.BrushToString(BullishBrush); }
			set { BullishBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Bearish MSS Color", Order = 7, GroupName = "Visuals")]
		public Brush BearishBrush { get; set; }

		[Browsable(false)]
		public string BearishBrushSerialize
		{
			get { return Serialize.BrushToString(BearishBrush); }
			set { BearishBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "BOS Color", Order = 8, GroupName = "Visuals")]
		public Brush BosBrush { get; set; }

		[Browsable(false)]
		public string BosBrushSerialize
		{
			get { return Serialize.BrushToString(BosBrush); }
			set { BosBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Swing Dot Color", Order = 9, GroupName = "Visuals")]
		public Brush SwingDotBrush { get; set; }

		[Browsable(false)]
		public string SwingDotBrushSerialize
		{
			get { return Serialize.BrushToString(SwingDotBrush); }
			set { SwingDotBrush = Serialize.StringToBrush(value); }
		}
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MavRixMSS[] cacheMavRixMSS;
		public MavRixMSS MavRixMSS(int swingStrength, bool showSwingPoints, bool showBOS, bool extendLevelLines, bool enableAlerts)
		{
			return MavRixMSS(Input, swingStrength, showSwingPoints, showBOS, extendLevelLines, enableAlerts);
		}

		public MavRixMSS MavRixMSS(ISeries<double> input, int swingStrength, bool showSwingPoints, bool showBOS, bool extendLevelLines, bool enableAlerts)
		{
			if (cacheMavRixMSS != null)
				for (int idx = 0; idx < cacheMavRixMSS.Length; idx++)
					if (cacheMavRixMSS[idx] != null && cacheMavRixMSS[idx].SwingStrength == swingStrength && cacheMavRixMSS[idx].ShowSwingPoints == showSwingPoints && cacheMavRixMSS[idx].ShowBOS == showBOS && cacheMavRixMSS[idx].ExtendLevelLines == extendLevelLines && cacheMavRixMSS[idx].EnableAlerts == enableAlerts && cacheMavRixMSS[idx].EqualsInput(input))
						return cacheMavRixMSS[idx];
			return CacheIndicator<MavRixMSS>(new MavRixMSS(){ SwingStrength = swingStrength, ShowSwingPoints = showSwingPoints, ShowBOS = showBOS, ExtendLevelLines = extendLevelLines, EnableAlerts = enableAlerts }, input, ref cacheMavRixMSS);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MavRixMSS MavRixMSS(int swingStrength, bool showSwingPoints, bool showBOS, bool extendLevelLines, bool enableAlerts)
		{
			return indicator.MavRixMSS(Input, swingStrength, showSwingPoints, showBOS, extendLevelLines, enableAlerts);
		}

		public Indicators.MavRixMSS MavRixMSS(ISeries<double> input , int swingStrength, bool showSwingPoints, bool showBOS, bool extendLevelLines, bool enableAlerts)
		{
			return indicator.MavRixMSS(input, swingStrength, showSwingPoints, showBOS, extendLevelLines, enableAlerts);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MavRixMSS MavRixMSS(int swingStrength, bool showSwingPoints, bool showBOS, bool extendLevelLines, bool enableAlerts)
		{
			return indicator.MavRixMSS(Input, swingStrength, showSwingPoints, showBOS, extendLevelLines, enableAlerts);
		}

		public Indicators.MavRixMSS MavRixMSS(ISeries<double> input , int swingStrength, bool showSwingPoints, bool showBOS, bool extendLevelLines, bool enableAlerts)
		{
			return indicator.MavRixMSS(input, swingStrength, showSwingPoints, showBOS, extendLevelLines, enableAlerts);
		}
	}
}

#endregion
