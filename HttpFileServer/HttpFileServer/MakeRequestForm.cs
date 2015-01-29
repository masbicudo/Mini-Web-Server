using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HttpFileServer
{
    public partial class MakeRequestForm : Form
    {
        public MakeRequestForm()
        {
            InitializeComponent();
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            try
            {
                this.Uri = new Uri(this.textBox1.Text);
            }
            catch
            {
            }
        }

        public Uri Uri { get; set; }

        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            this.ValidateUri();
        }

        private void ValidateUri()
        {
            this.textBox1.ForeColor =
                string.IsNullOrEmpty(this.textBox1.Text.Trim())
                || Uri.IsWellFormedUriString(this.textBox1.Text, UriKind.Absolute)
                    ? Color.FromKnownColor(KnownColor.WindowText)
                    : Color.Red;
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            this.ValidateUri();
        }

        private void textBox1_KeyUp(object sender, KeyEventArgs e)
        {
            this.ValidateUri();
        }
    }
}
