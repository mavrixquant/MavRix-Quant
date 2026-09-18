#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class MavRixSUPERTREND : Indicator
	{
		private ATR atr;
		private Series<double> upperBand;
		private Series<double> lowerBand;
		private Series<double> superTrendSeries;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description			= "Single White SuperTrend Indicator for Renko and Price Charts";
				Name				= "MavRixSUPERTREND";
				Calculate			= Calculate.OnBarClose;
				IsOverlay			= true;
				DisplayInDataBox	= true;
				DrawOnPricePanel	= true;
				DrawHorizontalGridLines = true;
				DrawVerticalGridLines = true;
				PaintPriceMarkers	= true;
				ScaleJustification	= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				
				BarsRequiredToPlot	= 10;
				
				// Default Inputs
				Period				= 10;
				Multiplier			= 3.0;
				
				// Single White Line Plot
				AddPlot(new Stroke(Brushes.White, 2), PlotStyle.Line, "SuperTrend");
			}
			else if (State == State.Configure)
			{
				atr = ATR(Period);
				upperBand = new Series<double>(this);
				lowerBand = new Series<double>(this);
				superTrendSeries = new Series<double>(this);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < Period) return;

			double medianPrice = (High[0] + Low[0]) / 2.0;
			double atrVal = atr[0] * Multiplier;

			double rawUpper = medianPrice + atrVal;
			double rawLower = medianPrice - atrVal;

			// Calculate Upper Band
			if (rawUpper < upperBand[1] || Close[1] > upperBand[1])
				upperBand[0] = rawUpper;
			else
				upperBand[0] = upperBand[1];

			// Calculate Lower Band
			if (rawLower > lowerBand[1] || Close[1] < lowerBand[1])
				lowerBand[0] = rawLower;
			else
				lowerBand[0] = lowerBand[1];

			// Determine SuperTrend Direction
			double prevSuperTrend = (CurrentBar <= Period) ? rawUpper : superTrendSeries[1];
			double currentSuperTrend = prevSuperTrend;

			if (prevSuperTrend == upperBand[1])
			{
				if (Close[0] > upperBand[0])
					currentSuperTrend = lowerBand[0];
				else
					currentSuperTrend = upperBand[0];
			}
			else
			{
				if (Close[0] < lowerBand[0])
					currentSuperTrend = upperBand[0];
				else
					currentSuperTrend = lowerBand[0];
			}

			superTrendSeries[0] = currentSuperTrend;

			// Assign everything to a single output value
			Values[0][0] = currentSuperTrend;
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Period", Order=1, GroupName="Parameters")]
		public int Period { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name="Multiplier", Order=2, GroupName="Parameters")]
		public double Multiplier { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MavRixSUPERTREND[] cacheMavRixSUPERTREND;
		public MavRixSUPERTREND MavRixSUPERTREND(int period, double multiplier)
		{
			return MavRixSUPERTREND(Input, period, multiplier);
		}

		public MavRixSUPERTREND MavRixSUPERTREND(ISeries<double> input, int period, double multiplier)
		{
			if (cacheMavRixSUPERTREND != null)
				for (int idx = 0; idx < cacheMavRixSUPERTREND.Length; idx++)
					if (cacheMavRixSUPERTREND[idx] != null && cacheMavRixSUPERTREND[idx].Period == period && cacheMavRixSUPERTREND[idx].Multiplier == multiplier && cacheMavRixSUPERTREND[idx].EqualsInput(input))
						return cacheMavRixSUPERTREND[idx];
			return CacheIndicator<MavRixSUPERTREND>(new MavRixSUPERTREND(){ Period = period, Multiplier = multiplier }, input, ref cacheMavRixSUPERTREND);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MavRixSUPERTREND MavRixSUPERTREND(int period, double multiplier)
		{
			return indicator.MavRixSUPERTREND(Input, period, multiplier);
		}

		public Indicators.MavRixSUPERTREND MavRixSUPERTREND(ISeries<double> input , int period, double multiplier)
		{
			return indicator.MavRixSUPERTREND(input, period, multiplier);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MavRixSUPERTREND MavRixSUPERTREND(int period, double multiplier)
		{
			return indicator.MavRixSUPERTREND(Input, period, multiplier);
		}

		public Indicators.MavRixSUPERTREND MavRixSUPERTREND(ISeries<double> input , int period, double multiplier)
		{
			return indicator.MavRixSUPERTREND(input, period, multiplier);
		}
	}
}

#endregion
