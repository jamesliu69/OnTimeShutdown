#nullable enable
namespace Showdown
{
	internal partial class ShutdownTimeDialog
	{
		private System.ComponentModel.IContainer? components = null;
		private System.Windows.Forms.NumericUpDown hourNumericUpDown = null!;
		private System.Windows.Forms.NumericUpDown minuteNumericUpDown = null!;
		private System.Windows.Forms.NumericUpDown secondNumericUpDown = null!;
		private System.Windows.Forms.Button okButton = null!;
		private System.Windows.Forms.Button cancelButton = null!;

		protected override void Dispose(bool disposing)
		{
			if(disposing)
				components?.Dispose();
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			var layout = new System.Windows.Forms.TableLayoutPanel();
			var timeLayout = new System.Windows.Forms.FlowLayoutPanel();
			var buttonLayout = new System.Windows.Forms.FlowLayoutPanel();
			this.hourNumericUpDown = new System.Windows.Forms.NumericUpDown();
			this.minuteNumericUpDown = new System.Windows.Forms.NumericUpDown();
			this.secondNumericUpDown = new System.Windows.Forms.NumericUpDown();
			this.okButton = new System.Windows.Forms.Button();
			this.cancelButton = new System.Windows.Forms.Button();
			((System.ComponentModel.ISupportInitialize)(this.hourNumericUpDown)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.minuteNumericUpDown)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.secondNumericUpDown)).BeginInit();
			this.SuspendLayout();
			//
			// Time fields
			//
			timeLayout.AutoSize = true;
			timeLayout.Dock = System.Windows.Forms.DockStyle.Fill;
			timeLayout.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
			timeLayout.WrapContents = false;
			timeLayout.Padding = new System.Windows.Forms.Padding(8, 8, 8, 0);
			AddTimeField(timeLayout, "小時", "小時（0 到 23）", hourNumericUpDown, 23);
			AddTimeField(timeLayout, "分鐘", "分鐘（0 到 59）", minuteNumericUpDown, 59);
			AddTimeField(timeLayout, "秒", "秒（0 到 59）", secondNumericUpDown, 59);
			//
			// Buttons
			//
			buttonLayout.AutoSize = true;
			buttonLayout.Dock = System.Windows.Forms.DockStyle.Fill;
			buttonLayout.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
			buttonLayout.Padding = new System.Windows.Forms.Padding(8, 8, 8, 8);
			okButton.AutoSize = true;
			okButton.MinimumSize = new System.Drawing.Size(84, 32);
			okButton.Text = "確定";
			okButton.AccessibleName = "確定關機時間";
			okButton.DialogResult = System.Windows.Forms.DialogResult.OK;
			cancelButton.AutoSize = true;
			cancelButton.MinimumSize = new System.Drawing.Size(84, 32);
			cancelButton.Text = "取消";
			cancelButton.AccessibleName = "取消修改";
			cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			buttonLayout.Controls.Add(okButton);
			buttonLayout.Controls.Add(cancelButton);
			//
			// Layout
			//
			layout.AutoSize = true;
			layout.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			layout.ColumnCount = 1;
			layout.RowCount = 2;
			layout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
			layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
			layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
			layout.Dock = System.Windows.Forms.DockStyle.Fill;
			layout.Controls.Add(timeLayout, 0, 0);
			layout.Controls.Add(buttonLayout, 0, 1);
			this.Controls.Add(layout);
			this.AcceptButton = okButton;
			this.CancelButton = cancelButton;
			this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoSize = true;
			this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "修改關機時間";
			this.AccessibleName = "修改關機時間";
			((System.ComponentModel.ISupportInitialize)(this.hourNumericUpDown)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.minuteNumericUpDown)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.secondNumericUpDown)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		private static void AddTimeField(System.Windows.Forms.FlowLayoutPanel panel, string labelText,
			string accessibleName, System.Windows.Forms.NumericUpDown control, int maximum)
		{
			var field = new System.Windows.Forms.FlowLayoutPanel
			{
				AutoSize = true,
				FlowDirection = System.Windows.Forms.FlowDirection.TopDown,
				WrapContents = false,
				Margin = new System.Windows.Forms.Padding(4),
			};
			var label = new System.Windows.Forms.Label
			{
				AutoSize = true,
				Text = labelText,
				AccessibleName = labelText,
				Margin = new System.Windows.Forms.Padding(0, 0, 0, 4),
			};
			control.Minimum = 0;
			control.Maximum = maximum;
			control.Width = 76;
			control.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
			control.AccessibleName = accessibleName;
			control.TabIndex = panel.Controls.Count;
			field.Controls.Add(label);
			field.Controls.Add(control);
			panel.Controls.Add(field);
		}
	}
}
