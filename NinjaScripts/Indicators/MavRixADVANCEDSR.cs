#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript
{
	public enum SRCalculationMethod
	{
		SwingPivots,
		BrickClustering,
		MultiTouchZones,
		CombinedConfluence
	}
}

namespace NinjaTrader.NinjaScript.Indicators
{
	public class MavRixADVANCEDSR : Indicator
	{
		private List<double> supportLevels;
		private List<double> resistanceLevels;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description             = "Advanced Major Support & Resistance Indicator optimized for 20-brick WickedRenko and Renko charts.";
				Name                    = "MavRixADVANCEDSR";
				Calculate               = Calculate.OnBarClose;
				IsOverlay               = true;
				DisplayInDataBox        = true;
				DrawOnPricePanel        = true;
				DrawHorizontalGridLines = true;
				DrawVerticalGridLines   = true;
				PaintPriceMarkers       = true;
				ScaleJustification      = ScaleJustification.Right;
				BarsRequiredToPlot      = 50;

				Method                  = SRCalculationMethod.CombinedConfluence;
				PivotStrength           = 8;  // Increased for Major structural swings
				BrickSizeTicks          = 20;
				MaxLevels               = 5;
				ZoneThicknessTicks      = 2;
				TouchThreshold          = 2;
				MinLevelDistanceTicks   = 40; // Ensures major spacing between levels (2 full 20-brick renko blocks)
				SupportColor            = Brushes.LimeGreen;
				ResistanceColor         = Brushes.Crimson;
				LineOpacity             = 75;
			}
			else if (State == State.Configure)
			{
				supportLevels = new List<double>();
				resistanceLevels = new List<double>();
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < Math.Max(50, PivotStrength * 2 + 1))
				return;

			supportLevels.Clear();
			resistanceLevels.Clear();

			double tickSize = TickSize;
			double brickSizeValue = BrickSizeTicks * tickSize;
			double currentPrice = Close[0];
			double minDistance = MinLevelDistanceTicks * tickSize;

			// Helper to classify levels and enforce minimum distance spacing (Major S&R)
			Action<double> addLevel = (double level) =>
			{
				if (level > currentPrice)
				{
					bool tooClose = resistanceLevels.Any(existing => Math.Abs(existing - level) < minDistance);
					if (!tooClose)
						resistanceLevels.Add(level);
				}
				else if (level < currentPrice)
				{
					bool tooClose = supportLevels.Any(existing => Math.Abs(existing - level) < minDistance);
					if (!tooClose)
						supportLevels.Add(level);
				}
			};

			// 1. Major Swing Pivot Analysis
			if (Method == SRCalculationMethod.SwingPivots || Method == SRCalculationMethod.CombinedConfluence)
			{
				for (int i = PivotStrength + 1; i <= CurrentBar - PivotStrength; i++)
				{
					if (i >= Count) break;
					bool isPivotHigh = true;
					bool isPivotLow = true;

					for (int p = 1; p <= PivotStrength; p++)
					{
						if (High[i] <= High[i - p] || High[i] <= High[i + p])
							isPivotHigh = false;
						if (Low[i] >= Low[i - p] || Low[i] >= Low[i + p])
							isPivotLow = false;
					}

					if (isPivotHigh)
						addLevel(High[i]);
					if (isPivotLow)
						addLevel(Low[i]);
				}
			}

			// 2. Brick Clustering Analysis (Aligned to WickedRenko / Renko 20-brick grid across larger history)
			if (Method == SRCalculationMethod.BrickClustering || Method == SRCalculationMethod.CombinedConfluence)
			{
				Dictionary<double, int> brickCounts = new Dictionary<double, int>();
				for (int i = 0; i < Math.Min(CurrentBar, 600); i++)
				{
					double roundedHigh = Math.Round(High[i] / brickSizeValue) * brickSizeValue;
					double roundedLow = Math.Round(Low[i] / brickSizeValue) * brickSizeValue;

					if (!brickCounts.ContainsKey(roundedHigh)) brickCounts[roundedHigh] = 0;
					brickCounts[roundedHigh]++;

					if (!brickCounts.ContainsKey(roundedLow)) brickCounts[roundedLow] = 0;
					brickCounts[roundedLow]++;
				}

				foreach (var kvp in brickCounts.OrderByDescending(x => x.Value))
				{
					if (kvp.Value >= TouchThreshold)
					{
						addLevel(kvp.Key);
					}
				}
			}

			// 3. Multi-Touch Zone Analysis across larger history
			if (Method == SRCalculationMethod.MultiTouchZones || Method == SRCalculationMethod.CombinedConfluence)
			{
				double tolerance = ZoneThicknessTicks * tickSize;
				Dictionary<double, int> touchCounts = new Dictionary<double, int>();

				for (int i = 0; i < Math.Min(CurrentBar, 500); i++)
				{
					double levelTest = Close[i];
					bool matched = false;
					foreach (var key in touchCounts.Keys.ToList())
					{
						if (Math.Abs(levelTest - key) <= tolerance)
						{
							touchCounts[key]++;
							matched = true;
							break;
						}
					}
					if (!matched)
					{
						touchCounts[levelTest] = 1;
					}
				}

				foreach (var kvp in touchCounts.OrderByDescending(x => x.Value))
				{
					if (kvp.Value >= TouchThreshold)
					{
						addLevel(kvp.Key);
					}
				}
			}

			// Sort and limit active major levels
			resistanceLevels = resistanceLevels.OrderBy(r => r).Take(MaxLevels).ToList();
			supportLevels = supportLevels.OrderByDescending(s => s).Take(MaxLevels).ToList();

			int lookbackBars = Math.Min(CurrentBar, 500);

			// Draw major levels on chart (extending into right margin with -10 bars ago)
			int levelIdx = 0;
			foreach (var sup in supportLevels)
			{
				string tag = Name + "_Support_" + levelIdx;
				Draw.Line(this, tag, false, lookbackBars, sup, -10, sup, SupportColor, DashStyleHelper.Solid, 2);
				levelIdx++;
			}

			levelIdx = 0;
			foreach (var res in resistanceLevels)
			{
				string tag = Name + "_Resistance_" + levelIdx;
				Draw.Line(this, tag, false, lookbackBars, res, -10, res, ResistanceColor, DashStyleHelper.Solid, 2);
				levelIdx++;
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name="Calculation Method", Description="Method for S/R algorithm", Order=1, GroupName="Parameters")]
		public SRCalculationMethod Method { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Pivot Strength", Description="Bars left/right for major swing pivot detection (Default: 8)", Order=2, GroupName="Parameters")]
		public int PivotStrength { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Brick Size (Ticks)", Description="WickedRenko / Renko brick size in ticks (Default: 20)", Order=3, GroupName="Parameters")]
		public int BrickSizeTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name="Max Levels", Description="Max number of Major S/R levels displayed", Order=4, GroupName="Parameters")]
		public int MaxLevels { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name="Zone Thickness (Ticks)", Description="Tolerance buffer in ticks", Order=5, GroupName="Parameters")]
		public int ZoneThicknessTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name="Touch Threshold", Description="Minimum touches required for level validation", Order=6, GroupName="Parameters")]
		public int TouchThreshold { get; set; }

		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name="Min Level Distance (Ticks)", Description="Minimum tick spacing between S/R lines to eliminate minor crowding (Default: 40)", Order=7, GroupName="Parameters")]
		public int MinLevelDistanceTicks { get; set; }

		[XmlIgnore]
		[Display(Name="Support Color", Order=1, GroupName="Appearance")]
		public Brush SupportColor { get; set; }

		[Browsable(false)]
		public string SupportColorSerializable
		{
			get { return Serialize.BrushToString(SupportColor); }
			set { SupportColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Resistance Color", Order=2, GroupName="Appearance")]
		public Brush ResistanceColor { get; set; }

		[Browsable(false)]
		public string ResistanceColorSerializable
		{
			get { return Serialize.BrushToString(ResistanceColor); }
			set { ResistanceColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name="Line Opacity", Order=3, GroupName="Appearance")]
		public int LineOpacity { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MavRixADVANCEDSR[] cacheMavRixADVANCEDSR;
		public MavRixADVANCEDSR MavRixADVANCEDSR(SRCalculationMethod method, int pivotStrength, int brickSizeTicks, int maxLevels, int zoneThicknessTicks, int touchThreshold, int minLevelDistanceTicks, int lineOpacity)
		{
			return MavRixADVANCEDSR(Input, method, pivotStrength, brickSizeTicks, maxLevels, zoneThicknessTicks, touchThreshold, minLevelDistanceTicks, lineOpacity);
		}

		public MavRixADVANCEDSR MavRixADVANCEDSR(ISeries<double> input, SRCalculationMethod method, int pivotStrength, int brickSizeTicks, int maxLevels, int zoneThicknessTicks, int touchThreshold, int minLevelDistanceTicks, int lineOpacity)
		{
			if (cacheMavRixADVANCEDSR != null)
				for (int idx = 0; idx < cacheMavRixADVANCEDSR.Length; idx++)
					if (cacheMavRixADVANCEDSR[idx] != null && cacheMavRixADVANCEDSR[idx].Method == method && cacheMavRixADVANCEDSR[idx].PivotStrength == pivotStrength && cacheMavRixADVANCEDSR[idx].BrickSizeTicks == brickSizeTicks && cacheMavRixADVANCEDSR[idx].MaxLevels == maxLevels && cacheMavRixADVANCEDSR[idx].ZoneThicknessTicks == zoneThicknessTicks && cacheMavRixADVANCEDSR[idx].TouchThreshold == touchThreshold && cacheMavRixADVANCEDSR[idx].MinLevelDistanceTicks == minLevelDistanceTicks && cacheMavRixADVANCEDSR[idx].LineOpacity == lineOpacity && cacheMavRixADVANCEDSR[idx].EqualsInput(input))
						return cacheMavRixADVANCEDSR[idx];
			return CacheIndicator<MavRixADVANCEDSR>(new MavRixADVANCEDSR(){ Method = method, PivotStrength = pivotStrength, BrickSizeTicks = brickSizeTicks, MaxLevels = maxLevels, ZoneThicknessTicks = zoneThicknessTicks, TouchThreshold = touchThreshold, MinLevelDistanceTicks = minLevelDistanceTicks, LineOpacity = lineOpacity }, input, ref cacheMavRixADVANCEDSR);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MavRixADVANCEDSR MavRixADVANCEDSR(SRCalculationMethod method, int pivotStrength, int brickSizeTicks, int maxLevels, int zoneThicknessTicks, int touchThreshold, int minLevelDistanceTicks, int lineOpacity)
		{
			return indicator.MavRixADVANCEDSR(Input, method, pivotStrength, brickSizeTicks, maxLevels, zoneThicknessTicks, touchThreshold, lineOpacity);
		}

		public Indicators.MavRixADVANCEDSR MavRixADVANCEDSR(ISeries<double> input, SRCalculationMethod method, int pivotStrength, int brickSizeTicks, int maxLevels, int zoneThicknessTicks, int touchThreshold, int minLevelDistanceTicks, int lineOpacity)
		{
			return indicator.MavRixADVANCEDSR(input, method, pivotStrength, brickSizeTicks, maxLevels, zoneThicknessTicks, touchThreshold, lineOpacity);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MavRixADVANCEDSR MavRixADVANCEDSR(SRCalculationMethod method, int pivotStrength, int brickSizeTicks, int maxLevels, int zoneThicknessTicks, int touchThreshold, int minLevelDistanceTicks, int lineOpacity)
		{
			return indicator.MavRixADVANCEDSR(Input, method, pivotStrength, brickSizeTicks, maxLevels, zoneThicknessTicks, touchThreshold, lineOpacity);
		}

		public Indicators.MavRixADVANCEDSR MavRixADVANCEDSR(ISeries<double> input, SRCalculationMethod method, int pivotStrength, int brickSizeTicks, int maxLevels, int zoneThicknessTicks, int touchThreshold, int minLevelDistanceTicks, int lineOpacity)
		{
			return indicator.MavRixADVANCEDSR(input, method, pivotStrength, brickSizeTicks, maxLevels, zoneThicknessTicks, touchThreshold, lineOpacity);
		}
	}
}

#endregion
