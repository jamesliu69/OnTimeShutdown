using System;
using System.Windows.Forms;

namespace Showdown
{
	internal partial class ShutdownTimeDialog : Form
	{
		public int Hour => (int)hourNumericUpDown.Value;

		public int Minute => (int)minuteNumericUpDown.Value;
		public int Second => (int)secondNumericUpDown.Value;

		public ShutdownTimeDialog(int hour, int minute, int second)
		{
			InitializeComponent();

			hourNumericUpDown.Value = Clamp(hour, 0, 23);
			minuteNumericUpDown.Value = Clamp(minute, 0, 59);
			secondNumericUpDown.Value = Clamp(second, 0, 59);
		}

		private static decimal Clamp(int value, int min, int max)
			=> Math.Clamp(value, min, max);
	}
}
