// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System.ComponentModel;
using System.Windows.Forms;

namespace hMailServer.Administrator.Dialogs
{
   public partial class formInfoDialog : Form
   {
      public formInfoDialog()
      {
         InitializeComponent();
         Strings.Localize(this);
      }

      [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
      public string Title
      {
         set
         {
            this.Text = value;
            Strings.Localize(this);
         }
         get
         {
            return this.Text;
         }
      }

      [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
      public string Message
      {
         set
         {
            textMessage.Text = value;
            Strings.Localize(this);
         }
         get
         {
            return textMessage.Text;
         }
      }
   }
}