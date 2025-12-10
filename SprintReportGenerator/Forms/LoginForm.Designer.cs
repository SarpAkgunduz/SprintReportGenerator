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
            txtUserName = new TextBox();
            btnContinue = new Button();
            EmailLabel = new Label();
            chkRememberMe = new CheckBox();
            txtSecret = new TextBox();
            ApiToken = new Label();
            txtJiraUrl = new TextBox();
            JiraUrlLabel = new Label();
            chkIsBasic = new CheckBox();
            SuspendLayout();
            // 
            // txtUserName
            // 
            txtUserName.AccessibleName = "";
            txtUserName.Location = new Point(256, 130);
            txtUserName.Name = "txtUserName";
            txtUserName.Size = new Size(250, 27);
            txtUserName.TabIndex = 0;
            // 
            // btnContinue
            // 
            btnContinue.Location = new Point(348, 368);
            btnContinue.Name = "btnContinue";
            btnContinue.Size = new Size(88, 32);
            btnContinue.TabIndex = 1;
            btnContinue.Text = "Continue";
            btnContinue.UseCompatibleTextRendering = true;
            btnContinue.UseVisualStyleBackColor = true;
            btnContinue.Click += btnContinue_Click;
            // 
            // EmailLabel
            // 
            EmailLabel.AutoSize = true;
            EmailLabel.Location = new Point(83, 133);
            EmailLabel.Name = "EmailLabel";
            EmailLabel.Size = new Size(82, 20);
            EmailLabel.TabIndex = 2;
            EmailLabel.Text = "User name:";
            // 
            // chkRememberMe
            // 
            chkRememberMe.AutoSize = true;
            chkRememberMe.Location = new Point(256, 284);
            chkRememberMe.Name = "chkRememberMe";
            chkRememberMe.Size = new Size(129, 24);
            chkRememberMe.TabIndex = 3;
            chkRememberMe.Text = "Remember me";
            chkRememberMe.UseVisualStyleBackColor = true;
            // 
            // txtSecret
            // 
            txtSecret.Location = new Point(256, 183);
            txtSecret.Name = "txtSecret";
            txtSecret.Size = new Size(250, 27);
            txtSecret.TabIndex = 4;
            txtSecret.UseSystemPasswordChar = true;
            // 
            // ApiToken
            // 
            ApiToken.AutoSize = true;
            ApiToken.Location = new Point(83, 186);
            ApiToken.Name = "ApiToken";
            ApiToken.Size = new Size(139, 20);
            ApiToken.TabIndex = 5;
            ApiToken.Text = "API token/Password";
            // 
            // txtJiraUrl
            // 
            txtJiraUrl.Location = new Point(256, 234);
            txtJiraUrl.Name = "txtJiraUrl";
            txtJiraUrl.Size = new Size(250, 27);
            txtJiraUrl.TabIndex = 6;
            // 
            // JiraUrlLabel
            // 
            JiraUrlLabel.AutoSize = true;
            JiraUrlLabel.Location = new Point(83, 234);
            JiraUrlLabel.Name = "JiraUrlLabel";
            JiraUrlLabel.Size = new Size(61, 20);
            JiraUrlLabel.TabIndex = 7;
            JiraUrlLabel.Text = "Jira URL";
            // 
            // chkIsBasic
            // 
            chkIsBasic.AutoSize = true;
            chkIsBasic.Location = new Point(256, 322);
            chkIsBasic.Name = "chkIsBasic";
            chkIsBasic.Size = new Size(159, 24);
            chkIsBasic.TabIndex = 8;
            chkIsBasic.Text = "Basic Authorization";
            chkIsBasic.UseVisualStyleBackColor = true;
            // 
            // LoginForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(chkIsBasic);
            Controls.Add(JiraUrlLabel);
            Controls.Add(txtJiraUrl);
            Controls.Add(ApiToken);
            Controls.Add(txtSecret);
            Controls.Add(chkRememberMe);
            Controls.Add(EmailLabel);
            Controls.Add(btnContinue);
            Controls.Add(txtUserName);
            Name = "LoginForm";
            Text = "LoginForm";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox txtUserName;
        private Button btnContinue;
        private Label EmailLabel;
        private CheckBox chkRememberMe;
        private TextBox txtSecret;
        private Label ApiToken;
        private TextBox txtJiraUrl;
        private Label JiraUrlLabel;
        private CheckBox chkIsBasic;

    }
}