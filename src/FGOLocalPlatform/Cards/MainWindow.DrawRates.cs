using System;
using System.Windows;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private SummonSettingsWindow? summonSettingsPanel;

	private void SummonTab_OnSelected(object sender, RoutedEventArgs e)
	{
		if (sender != e.OriginalSource || summonSettingsPanel != null)
		{
			return;
		}
		try
		{
			summonSettingsPanel = new SummonSettingsWindow(GamePaths.GameRoot);
			SummonTabContent.Content = summonSettingsPanel;
			SummonTabProblemText.Visibility = Visibility.Collapsed;
		}
		catch (Exception ex)
		{
			SummonTabProblemText.Text = "อัตราการสุ่มเป็นการตั้งค่าของเซิร์ฟเวอร์ในเครื่อง และอ่านไฟล์ของเซิร์ฟเวอร์ไม่ได้ จึงไม่มีอะไรให้เปลี่ยนที่นี่ การเล่นออนไลน์ใช้อัตราของเซิร์ฟเวอร์ชุมชนเอง\n\n" + ex.Message;
			SummonTabProblemText.Visibility = Visibility.Visible;
		}
	}
}
