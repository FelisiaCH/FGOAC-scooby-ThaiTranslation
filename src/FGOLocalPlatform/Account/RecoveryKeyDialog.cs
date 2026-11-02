using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Markup;
using Microsoft.Win32;

namespace FGOLocalPlatform;

public partial class RecoveryKeyDialog : Window, IComponentConnector
{
	private readonly string host;

	private bool kept;

	/// <summary>Shows a new account's key, or with recovered set the key a recovery rotated to.</summary>
	public RecoveryKeyDialog(Window owner, string host, string recoveryKey, bool recovered)
	{
		InitializeComponent();
		WindowTheme.Apply(this);
		base.Owner = owner;
		this.host = host;
		HeadingText.Text = recovered ? ("บัญชีของคุณบน " + host + " กลับมาอยู่บนเครื่องนี้แล้ว") : ("บัญชีของคุณบน " + host + " พร้อมใช้งานแล้ว");
		NoticeText.Text = Notice(recovered);
		KeyTextBox.Text = recoveryKey;
		KeyTextBox.SelectAll();
		KeyTextBox.Focus();
	}

	public static string Notice(bool recovered)
	{
		string notice = "This key is the only way to get the account back if this PC loses it, and it is not shown again.";
		return recovered ? ("Recovering gave the account this new key, and the previous key no longer works. " + notice) : notice;
	}

	private void CopyButton_OnClick(object sender, RoutedEventArgs e)
	{
		try
		{
			Clipboard.SetText(KeyTextBox.Text);
			kept = true;
		}
		catch (ExternalException ex)
		{
			ThemedMessageBox.Show(this, "คัดลอกคีย์ไม่ได้: " + ex.Message + "\nเลือกคีย์แล้วกด Ctrl+C หรือใช้ปุ่ม บันทึกเป็นไฟล์", "คีย์กู้คืน", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void SaveButton_OnClick(object sender, RoutedEventArgs e)
	{
		SaveFileDialog dialog = new SaveFileDialog
		{
			Title = "บันทึกคีย์กู้คืน",
			InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			FileName = "FGOAC recovery key " + host + ".txt",
			Filter = "ไฟล์ข้อความ (*.txt)|*.txt",
			DefaultExt = ".txt",
			AddExtension = true
		};
		if (DialogSink.ShowDialog(dialog, this) != true)
		{
			return;
		}
		try
		{
			File.WriteAllText(dialog.FileName, "Community server: " + host + "\r\nRecovery key: " + KeyTextBox.Text + "\r\n");
			kept = true;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			ThemedMessageBox.Show(this, "บันทึกคีย์ไม่ได้: " + ex.Message + "\nเลือกโฟลเดอร์อื่น หรือคัดลอกคีย์แทน", "คีย์กู้คืน", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void DoneButton_OnClick(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void Dialog_OnClosing(object? sender, CancelEventArgs e)
	{
		if (!kept && ThemedMessageBox.Show(this, "ปิดโดยไม่บันทึกคีย์กู้คืนใช่ไหม คีย์นี้จะไม่แสดงอีก", "คีย์กู้คืน", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			e.Cancel = true;
		}
	}
}
