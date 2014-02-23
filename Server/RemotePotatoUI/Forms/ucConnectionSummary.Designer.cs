namespace RemotePotatoServer
{
    partial class ucConnectionSummary
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.label3 = new System.Windows.Forms.Label();
            this.llAddFirewall = new System.Windows.Forms.LinkLabel();
            this.lblLANSettings = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.groupBox1.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.label2);
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Controls.Add(this.llAddFirewall);
            this.groupBox1.Controls.Add(this.lblLANSettings);
            this.groupBox1.Controls.Add(this.label5);
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Location = new System.Drawing.Point(12, 3);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(557, 272);
            this.groupBox1.TabIndex = 1;
            this.groupBox1.TabStop = false;
            // 
            // label3
            // 
            this.label3.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label3.Location = new System.Drawing.Point(19, 118);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(102, 22);
            this.label3.TabIndex = 34;
            this.label3.Text = "Port:";
            // 
            // llAddFirewall
            // 
            this.llAddFirewall.AutoSize = true;
            this.llAddFirewall.Cursor = System.Windows.Forms.Cursors.Hand;
            this.llAddFirewall.Location = new System.Drawing.Point(413, 246);
            this.llAddFirewall.Name = "llAddFirewall";
            this.llAddFirewall.Size = new System.Drawing.Size(112, 13);
            this.llAddFirewall.TabIndex = 32;
            this.llAddFirewall.TabStop = true;
            this.llAddFirewall.Text = "Connection problems?";
            this.llAddFirewall.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.llAddFirewall_LinkClicked);
            // 
            // lblLANSettings
            // 
            this.lblLANSettings.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblLANSettings.Location = new System.Drawing.Point(184, 58);
            this.lblLANSettings.Name = "lblLANSettings";
            this.lblLANSettings.Size = new System.Drawing.Size(226, 91);
            this.lblLANSettings.TabIndex = 8;
            this.lblLANSettings.Text = "**LAN-ADDRESS**\r\n**WAN-ADDRESS**\r\n**LAN-PORT**\r\n\r\n**EXTLAN-PORT**";
            // 
            // label5
            // 
            this.label5.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label5.Location = new System.Drawing.Point(19, 58);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(159, 53);
            this.label5.TabIndex = 6;
            this.label5.Text = "At home (LAN):\r\nRemotely (WAN):\r\nPort:";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 14.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.Location = new System.Drawing.Point(18, 20);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(359, 24);
            this.label1.TabIndex = 1;
            this.label1.Text = "Connecting to Remote Potato Live TV";
            // 
            // label2
            // 
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(19, 149);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(478, 88);
            this.label2.TabIndex = 35;
            this.label2.Text = "e.g. in a browser enter \r\n\r\nhttp://**WAN-ADDRESS**:**EXTLAN-PORT**\r\n\r\nin the addr" +
    "ess bar.";
            // 
            // ucConnectionSummary
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.groupBox1);
            this.Name = "ucConnectionSummary";
            this.Size = new System.Drawing.Size(722, 697);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Label lblLANSettings;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.LinkLabel llAddFirewall;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
    }
}
