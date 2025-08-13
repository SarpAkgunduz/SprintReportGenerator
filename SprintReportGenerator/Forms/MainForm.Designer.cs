namespace SprintReportGenerator.Forms
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            btnGenerate = new Button();
            txtSprintName = new TextBox();
            Sprint = new Label();
            SuspendLayout();
            // 
            // btnGenerate
            // 
            btnGenerate.Location = new Point(296, 285);
            btnGenerate.Name = "btnGenerate";
            btnGenerate.Size = new Size(205, 29);
            btnGenerate.TabIndex = 0;
            btnGenerate.Text = "Generate Report";
            btnGenerate.UseVisualStyleBackColor = true;
            // 
            // txtSprintName
            // 
            txtSprintName.Location = new Point(190, 79);
            txtSprintName.Name = "txtSprintName";
            txtSprintName.Size = new Size(125, 27);
            txtSprintName.TabIndex = 1;
            // 
            // Sprint
            // 
            Sprint.AutoSize = true;
            Sprint.Location = new Point(112, 82);
            Sprint.Name = "Sprint";
            Sprint.Size = new Size(51, 20);
            Sprint.TabIndex = 2;
            Sprint.Text = "Sprint:";
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(Sprint);
            Controls.Add(txtSprintName);
            Controls.Add(btnGenerate);
            Name = "MainForm";
            Text = "Form1";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button btnGenerate;
        private TextBox txtSprintName;
        private Label Sprint;
    }
}