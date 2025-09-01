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
            chkRememberMe = new CheckBox();
            txtApiToken = new TextBox();
            ApiToken = new Label();
            txtJiraUrl = new TextBox();
            JiraUrlLabel = new Label();
            SuspendLayout();
            // 
            // txtEmail
            // 
            txtEmail.AccessibleName = "";
            txtEmail.Location = new Point(256, 130);
            txtEmail.Name = "txtEmail";
            txtEmail.Size = new Size(250, 27);
            txtEmail.TabIndex = 0;
            // 
            // btnContinue
            // 
            btnContinue.Location = new Point(256, 324);
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
            EmailLabel.Size = new Size(145, 20);
            EmailLabel.TabIndex = 2;
            EmailLabel.Text = "Enter your jira email:";
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
            // txtApiToken
            // 
            txtApiToken.Location = new Point(256, 183);
            txtApiToken.Name = "txtApiToken";
            txtApiToken.Size = new Size(250, 27);
            txtApiToken.TabIndex = 4;
            txtApiToken.UseSystemPasswordChar = true;
            // 
            // ApiToken
            // 
            ApiToken.AutoSize = true;
            ApiToken.Location = new Point(83, 186);
            ApiToken.Name = "ApiToken";
            ApiToken.Size = new Size(170, 20);
            ApiToken.TabIndex = 5;
            ApiToken.Text = "Enter your jira api token:";
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
            JiraUrlLabel.Size = new Size(160, 20);
            JiraUrlLabel.TabIndex = 7;
            JiraUrlLabel.Text = "Enter your base jira url:";
            // 
            // LoginForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(JiraUrlLabel);
            Controls.Add(txtJiraUrl);
            Controls.Add(ApiToken);
            Controls.Add(txtApiToken);
            Controls.Add(chkRememberMe);
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
        private CheckBox chkRememberMe;
        private TextBox txtApiToken;
        private Label ApiToken;
        private TextBox txtJiraUrl;
        private Label JiraUrlLabel;
    }
}