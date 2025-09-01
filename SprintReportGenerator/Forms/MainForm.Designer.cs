using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SprintReportGenerator.Forms
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private IContainer components = null;

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
            txtMemberName = new TextBox();
            txtMemberLabel = new Label();
            txtProjectName = new TextBox();
            txtProjectLabel = new Label();
            chkOpenAfterGenerate = new CheckBox();
            statusStrip1 = new StatusStrip();
            tsPadLeft = new ToolStripStatusLabel();
            tsProg = new ToolStripProgressBar();
            tsPadRight = new ToolStripStatusLabel();
            tslJira = new ToolStripStatusLabel();
            issueList = new ListView();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // btnGenerate
            // 
            btnGenerate.Location = new Point(237, 318);
            btnGenerate.Name = "btnGenerate";
            btnGenerate.Size = new Size(205, 29);
            btnGenerate.TabIndex = 0;
            btnGenerate.Text = "Generate Report";
            btnGenerate.UseVisualStyleBackColor = true;
            btnGenerate.Click += btnGenerate_Click;
            // 
            // txtSprintName
            // 
            txtSprintName.Location = new Point(237, 79);
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
            // txtMemberName
            // 
            txtMemberName.Location = new Point(237, 183);
            txtMemberName.Name = "txtMemberName";
            txtMemberName.Size = new Size(181, 27);
            txtMemberName.TabIndex = 3;
            // 
            // txtMemberLabel
            // 
            txtMemberLabel.AutoSize = true;
            txtMemberLabel.Location = new Point(112, 186);
            txtMemberLabel.Name = "txtMemberLabel";
            txtMemberLabel.Size = new Size(112, 20);
            txtMemberLabel.TabIndex = 4;
            txtMemberLabel.Text = "Member Name:";
            // 
            // txtProjectName
            // 
            txtProjectName.Location = new Point(237, 132);
            txtProjectName.Name = "txtProjectName";
            txtProjectName.Size = new Size(181, 27);
            txtProjectName.TabIndex = 5;
            // 
            // txtProjectLabel
            // 
            txtProjectLabel.AutoSize = true;
            txtProjectLabel.Location = new Point(112, 135);
            txtProjectLabel.Name = "txtProjectLabel";
            txtProjectLabel.Size = new Size(99, 20);
            txtProjectLabel.TabIndex = 6;
            txtProjectLabel.Text = "Project Name";
            // 
            // chkOpenAfterGenerate
            // 
            chkOpenAfterGenerate.AutoSize = true;
            chkOpenAfterGenerate.Location = new Point(237, 262);
            chkOpenAfterGenerate.Name = "chkOpenAfterGenerate";
            chkOpenAfterGenerate.Size = new Size(223, 24);
            chkOpenAfterGenerate.TabIndex = 7;
            chkOpenAfterGenerate.Text = "Open report after generation";
            chkOpenAfterGenerate.UseVisualStyleBackColor = true;
            // 
            // statusStrip1
            // 
            statusStrip1.ImageScalingSize = new Size(20, 20);
            statusStrip1.Items.AddRange(new ToolStripItem[] { tsPadLeft, tsProg, tsPadRight, tslJira });
            statusStrip1.Location = new Point(0, 424);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.ShowItemToolTips = true;
            statusStrip1.Size = new Size(800, 26);
            statusStrip1.TabIndex = 9;
            statusStrip1.Text = "statusStrip1";
            // 
            // tsPadLeft
            // 
            tsPadLeft.Name = "tsPadLeft";
            tsPadLeft.Size = new Size(338, 20);
            tsPadLeft.Spring = true;
            // 
            // tsProg
            // 
            tsProg.AutoSize = false;
            tsProg.MarqueeAnimationSpeed = 30;
            tsProg.Name = "tsProg";
            tsProg.Size = new Size(140, 18);
            tsProg.Style = ProgressBarStyle.Marquee;
            tsProg.Visible = false;
            // 
            // tsPadRight
            // 
            tsPadRight.Name = "tsPadRight";
            tsPadRight.Size = new Size(338, 20);
            tsPadRight.Spring = true;
            // 
            // tslJira
            // 
            tslJira.Alignment = ToolStripItemAlignment.Right;
            tslJira.Name = "tslJira";
            tslJira.Size = new Size(108, 20);
            tslJira.Text = "Jira: Not tested";
            // 
            // issueList
            // 
            issueList.Location = new Point(507, 52);
            issueList.Name = "issueList";
            issueList.Size = new Size(250, 326);
            issueList.TabIndex = 10;
            issueList.UseCompatibleStateImageBehavior = false;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(issueList);
            Controls.Add(statusStrip1);
            Controls.Add(chkOpenAfterGenerate);
            Controls.Add(txtProjectLabel);
            Controls.Add(txtProjectName);
            Controls.Add(txtMemberLabel);
            Controls.Add(txtMemberName);
            Controls.Add(Sprint);
            Controls.Add(txtSprintName);
            Controls.Add(btnGenerate);
            Name = "MainForm";
            Text = "Form1";
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button btnGenerate;
        private TextBox txtSprintName;
        private Label Sprint;
        private TextBox txtMemberName;
        private Label txtMemberLabel;
        private TextBox txtProjectName;
        private Label txtProjectLabel;
        private CheckBox chkOpenAfterGenerate;

        private StatusStrip statusStrip1;
        private ToolStripStatusLabel tsPadLeft;
        private ToolStripProgressBar tsProg;
        private ToolStripStatusLabel tsPadRight;
        private ToolStripStatusLabel tslJira;
        private ListView issueList;
    }
}
