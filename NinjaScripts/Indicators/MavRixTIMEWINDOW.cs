#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class MavRixTIMEBOX : Indicator
    {
        private int startHour = 12;
        private int startMinute = 30;
        private int endHour = 14;
        private int endMinute = 30;
		private int boxOpacity = 30;

        private Brush boxBrush = Brushes.Gold;
        private double sessionHigh = double.MinValue;
        private double sessionLow = double.MaxValue;
        private bool inWindow = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description             = @"Draws a solid rectangular box for a specified time window.";
                Name                    = "MavRixTIMEBOX";
                Calculate               = Calculate.OnBarClose;
                IsOverlay               = true;
                DisplayInDataBox        = false;
                DrawOnPricePanel        = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines   = true;
                PaintPriceMarkers       = true;
                ScaleJustification      = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                StartHour               = 12;
                StartMinute             = 30;
                EndHour                 = 14;
                EndMinute               = 30;

                BoxColor                = Brushes.Gold;
				BoxOpacity              = 30;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            DateTime barTime = Time[0];
            DateTime startTime = new DateTime(barTime.Year, barTime.Month, barTime.Day, StartHour, StartMinute, 0);
            DateTime endTime   = new DateTime(barTime.Year, barTime.Month, barTime.Day, EndHour, EndMinute, 0);

            // Safety for overnight windows
            if (endTime <= startTime)
                endTime = endTime.AddDays(1);

            string dateTag = barTime.TimeOfDay >= startTime.TimeOfDay && barTime.TimeOfDay <= endTime.TimeOfDay 
                             ? barTime.ToString("yyyyMMdd") 
                             : barTime.AddDays(1).ToString("yyyyMMdd"); // Keep unique identifier per session

            // Check if we are inside the target time window
            if (barTime.TimeOfDay >= startTime.TimeOfDay && barTime.TimeOfDay <= endTime.TimeOfDay)
            {
                if (!inWindow)
                {
                    // Reset high/low when entering the window for the first time
                    sessionHigh = High[0];
                    sessionLow = Low[0];
                    inWindow = true;
                }
                else
                {
                    // Track highest high and lowest low within the window
                    if (High[0] > sessionHigh) sessionHigh = High[0];
                    if (Low[0] < sessionLow) sessionLow = Low[0];
                }

                // Continuously draw/update the rectangle with flat top and bottom levels
                Draw.RegionHighlightX(this, "TimeBox_" + dateTag, startTime, endTime, null, BoxColor, BoxOpacity);
            }
            else
            {
                inWindow = false;
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Start Hour", Description = "Hour of the start time (0-23)", Order = 1, GroupName = "Time Window")]
        public int StartHour
        {
            get { return startHour; }
            set { startHour = value; }
        }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Start Minute", Description = "Minute of the start time (0-59)", Order = 2, GroupName = "Time Window")]
        public int StartMinute
        {
            get { return startMinute; }
            set { startMinute = value; }
        }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "End Hour", Description = "Hour of the end time (0-23)", Order = 3, GroupName = "Time Window")]
        public int EndHour
        {
            get { return endHour; }
            set { endHour = value; }
        }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "End Minute", Description = "Minute of the end time (0-59)", Order = 4, GroupName = "Time Window")]
        public int EndMinute
        {
            get { return endMinute; }
            set { endMinute = value; }
        }

        [XmlIgnore]
        [Display(Name = "Box Color", Description = "Color of the time window box", Order = 1, GroupName = "Appearance")]
        public Brush BoxColor
        {
            get { return boxBrush; }
            set { boxBrush = value; }
        }
		
		[NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Box Opacity", Description = "Opacity of the Box Colour", Order = 2, GroupName = "Appearance")]
        public int BoxOpacity
        {
            get { return boxOpacity; }
            set { boxOpacity = value; }
        }

        [Browsable(false)]
        public string BoxColorSerializable
        {
            get { return Serialize.BrushToString(boxBrush); }
            set { boxBrush = Serialize.StringToBrush(value); }
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MavRixTIMEBOX[] cacheMavRixTIMEBOX;
		public MavRixTIMEBOX MavRixTIMEBOX(int startHour, int startMinute, int endHour, int endMinute, int boxOpacity)
		{
			return MavRixTIMEBOX(Input, startHour, startMinute, endHour, endMinute, boxOpacity);
		}

		public MavRixTIMEBOX MavRixTIMEBOX(ISeries<double> input, int startHour, int startMinute, int endHour, int endMinute, int boxOpacity)
		{
			if (cacheMavRixTIMEBOX != null)
				for (int idx = 0; idx < cacheMavRixTIMEBOX.Length; idx++)
					if (cacheMavRixTIMEBOX[idx] != null && cacheMavRixTIMEBOX[idx].StartHour == startHour && cacheMavRixTIMEBOX[idx].StartMinute == startMinute && cacheMavRixTIMEBOX[idx].EndHour == endHour && cacheMavRixTIMEBOX[idx].EndMinute == endMinute && cacheMavRixTIMEBOX[idx].BoxOpacity == boxOpacity && cacheMavRixTIMEBOX[idx].EqualsInput(input))
						return cacheMavRixTIMEBOX[idx];
			return CacheIndicator<MavRixTIMEBOX>(new MavRixTIMEBOX(){ StartHour = startHour, StartMinute = startMinute, EndHour = endHour, EndMinute = endMinute, BoxOpacity = boxOpacity }, input, ref cacheMavRixTIMEBOX);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MavRixTIMEBOX MavRixTIMEBOX(int startHour, int startMinute, int endHour, int endMinute, int boxOpacity)
		{
			return indicator.MavRixTIMEBOX(Input, startHour, startMinute, endHour, endMinute, boxOpacity);
		}

		public Indicators.MavRixTIMEBOX MavRixTIMEBOX(ISeries<double> input , int startHour, int startMinute, int endHour, int endMinute, int boxOpacity)
		{
			return indicator.MavRixTIMEBOX(input, startHour, startMinute, endHour, endMinute, boxOpacity);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MavRixTIMEBOX MavRixTIMEBOX(int startHour, int startMinute, int endHour, int endMinute, int boxOpacity)
		{
			return indicator.MavRixTIMEBOX(Input, startHour, startMinute, endHour, endMinute, boxOpacity);
		}

		public Indicators.MavRixTIMEBOX MavRixTIMEBOX(ISeries<double> input , int startHour, int startMinute, int endHour, int endMinute, int boxOpacity)
		{
			return indicator.MavRixTIMEBOX(input, startHour, startMinute, endHour, endMinute, boxOpacity);
		}
	}
}

#endregion
