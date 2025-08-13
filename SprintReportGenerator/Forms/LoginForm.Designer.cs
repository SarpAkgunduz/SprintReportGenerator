namespace SprintReportGenerator.Forms
{
    partial class LoginForm
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
            txtEmail = new TextBox();
            btnContinue = new Button();
            EmailLabel = new Label();
            chkRemember = new CheckBox();
            SuspendLayout();
            // 
            // txtEmail
            // 
            txtEmail.AccessibleName = "";
            txtEmail.Location = new Point(256, 160);
            txtEmail.Name = "txtEmail";
            txtEmail.Size = new Size(250, 27);
            txtEmail.TabIndex = 0;
            // 
            // btnContinue
            // 
            btnContinue.Location = new Point(256, 257);
            btnContinue.Name = "btnContinue";
            btnContinue.Size = new Size(88, 32);
            btnContinue.TabIndex = 1;
            btnContinue.Text = "Continue";
            btnContinue.UseCompatibleTextRendering = true;
            btnContinue.UseVisualStyleBackColor = true;
            // 
            // EmailLabel
            // 
            EmailLabel.AutoSize = true;
            EmailLabel.Location = new Point(310, 125);
            EmailLabel.Name = "EmailLabel";
            EmailLabel.Size = new Size(143, 20);
            EmailLabel.TabIndex = 2;
            EmailLabel.Text = "Enter your Jira email";
            // 
            // chkRemember
            // 
            chkRemember.AutoSize = true;
            chkRemember.Location = new Point(256, 295);
            chkRemember.Name = "chkRemember";
            chkRemember.Size = new Size(129, 24);
            chkRemember.TabIndex = 3;
            chkRemember.Text = "Remember me";
            chkRemember.UseVisualStyleBackColor = true;
            chkRemember.CheckedChanged += checkBox1_CheckedChanged;
            // 
            // LoginForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(chkRemember);
            Controls.Add(EmailLabel);
            Controls.Add(btnContinue);
            Controls.Add(txtEmail);
            Name = "LoginForm";
            Text = "LoginForm";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox txtEmail;
        private Button btnContinue;
        private Label EmailLabel;
        private CheckBox chkRemember;
    }
}