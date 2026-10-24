// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System.ComponentModel;
using System;
using System.Windows.Forms;
using hMailServer.Shared;
namespace hMailServer.Administrator
{
   public partial class formInputDialog : Form
   {
      public formInputDialog()
      {
         InitializeComponent();

         new TabOrderManager(this).SetTabOrder(TabOrderManager.TabScheme.AcrossFirst);
         Strings.Localize(this);

         EnableDisable();
      }

      [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
      public string Title
      {
         set
         {
            // Assigned after the constructor has localized the form, so the
            // value has to be localized here instead.
            base.Text = Strings.Localize(value);
         }
         get
         {
            return base.Text;
         }
      }

      [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
      public new string Text
      {
         set
         {
            // Assigned after the constructor has localized the form, so the
            // value has to be localized here instead.
            labelText.Text = Strings.Localize(value);
         }
         get
         {
            return labelText.Text;
         }
      }

      [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
      public string Value
      {
         set
         {
            textValue.Text = value;

            EnableDisable();
         }
         get
         {
            return textValue.Text.Trim();
         }
      }

      private void btnOK_Click(object sender, EventArgs e)
      {
         
      }

      private void textValue_TextChanged(object sender, EventArgs e)
      {
         EnableDisable();
      }

      private void EnableDisable()
      {
         btnOK.Enabled = textValue.Text.Trim().Length > 0;
      }
  

   }
}