using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private int hideUiVirtualKey = 121;

	private bool bindingHideUiKey;

	private void UpdateHideUiKeyLabel()
	{
		HideUiKeyButton.Content = KeyInterop.KeyFromVirtualKey(hideUiVirtualKey).ToString();
	}

	private void HideUiKeyButton_OnClick(object sender, RoutedEventArgs e)
	{
		bindingHideUiKey = true;
		HideUiKeyButton.Content = "กดปุ่มใดปุ่มหนึ่ง (Esc เพื่อยกเลิก)";
		HideUiKeyButton.Focus();
	}

	private void HideUiKeyButton_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		bindingHideUiKey = false;
		UpdateHideUiKeyLabel();
	}

	private void HideUiKeyButton_OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (!bindingHideUiKey)
		{
			return;
		}
		e.Handled = true;
		Key val = ((e.Key == Key.System) ? e.SystemKey : e.Key);
		if (val != Key.LWin && val != Key.RWin && (val < Key.LeftShift || val > Key.RightAlt))
		{
			if (val != Key.Escape)
			{
				hideUiVirtualKey = KeyInterop.VirtualKeyFromKey(val);
			}
			bindingHideUiKey = false;
			UpdateHideUiKeyLabel();
		}
	}

	private void ShaderCacheButton_OnClick(object sender, RoutedEventArgs e)
	{
		bool running = false;
		foreach (string name in new string[2] { "ago", "amdaemon" })
		{
			Process[] processes = Process.GetProcessesByName(name);
			running |= processes.Length != 0;
			foreach (Process process in processes)
			{
				process.Dispose();
			}
		}
		try
		{
			HoldStatus((ShaderCache.Clear(GamePaths.GameRoot, running).Count == 0) ? "ไม่มีแคชเชเดอร์ให้ล้าง" : "ล้างแคชเชเดอร์แล้ว: การเริ่มเกมครั้งถัดไปจะสร้างใหม่ จึงใช้เวลานานขึ้นเล็กน้อย");
		}
		catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException)
		{
			HoldStatus(ex.Message, problem: true);
		}
	}

	private void SaveGraphicsButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (!SaveLauncherSettings(out string _, out int _, out int _, out string _, out int _))
		{
			return;
		}
		try
		{
			if (PickedGpuLayer is GpuCompat.Choice picked && picked != GpuCompat.Current)
			{
				GpuCompat.Apply(picked);
			}
		}
		catch (Exception ex)
		{
			HoldStatus("เปลี่ยนเลเยอร์ความเข้ากันได้ไม่ได้: " + ex.Message);
			return;
		}
		RefreshGpuCompatSection();
		ShowGpuCardOptions();
		try
		{
			ApplyGpuPreference();
		}
		catch (Exception ex)
		{
			HoldStatus("ตั้งค่ากำหนด GPU ไม่ได้: " + ex.Message);
			return;
		}
		HoldStatus("บันทึกการตั้งค่ากราฟิกแล้ว - จะมีผลเมื่อเริ่มเกมครั้งถัดไป");
	}

	/// <summary>The layer picked in the Graphics layer list; null while "Another layer" is shown.</summary>
	private GpuCompat.Choice? PickedGpuLayer => (GpuLayerComboBox.SelectedItem as ComboBoxItem)?.Tag as GpuCompat.Choice?;

	/// <summary>Fills the Graphics layer list with the choices this game folder offers and selects the layer in App\.</summary>
	private void RefreshGpuCompatSection()
	{
		GpuCompat.Choice? current = GpuCompat.Current;
		GpuLayerComboBox.Items.Clear();
		if (current == null)
		{
			GpuLayerComboBox.Items.Add(new ComboBoxItem { Content = "เลเยอร์อื่น ปล่อยไว้ตามเดิม", Tag = null });
		}
		foreach (GpuCompat.Choice choice in GpuCompat.Offered)
		{
			GpuLayerComboBox.Items.Add(new ComboBoxItem { Content = GpuLayerName(choice), Tag = choice });
		}
		GpuLayerComboBox.SelectedIndex = 0;
		foreach (ComboBoxItem item in GpuLayerComboBox.Items)
		{
			if (Equals(item.Tag, current))
			{
				GpuLayerComboBox.SelectedItem = item;
			}
		}
		ShowGpuLayerHelp();
	}

	private static string GpuLayerName(GpuCompat.Choice choice)
	{
		return choice switch
		{
			GpuCompat.Choice.Cloud => "Cloud23333's layer",
			GpuCompat.Choice.Older => "เลเยอร์ 1.1.0",
			GpuCompat.Choice.Fluphus => "เลเยอร์ AMD ของ fluphus",
			_ => "Off"
		};
	}

	private void GpuLayerComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		ShowGpuLayerHelp();
	}

	private void ShowGpuLayerHelp()
	{
		bool his = GpuCompat.HisBuild;
		GpuCompatHelpText.Text = PickedGpuLayer switch
		{
			null => "App มีไฟล์เลเยอร์ที่ FGOAC scooby ไม่ได้ติดตั้งให้ จึงปล่อยไว้ตามเดิม เลือกเลเยอร์เพื่อแทนที่",
			GpuCompat.Choice.Cloud => his ? "เลเยอร์ที่อัปเดตของ Cloud23333 ติดตั้งและใช้งาน เลเยอร์ AMD ของ fluphus เป็นอีกตัวเลือกสำหรับการ์ดจอที่ทำงานได้ไม่ดีบนเลเยอร์ของเขา" : "เลเยอร์ของ Cloud23333 จากอัปเดตล่าสุดของเขา (App\\fgoglcompat.dll) เป็นค่าเริ่มต้นสำหรับกราฟิก AMD และ Intel",
			GpuCompat.Choice.Older => "เลเยอร์ที่ FGOAC scooby ติดตั้งมากับ 1.1.0 (App\\fgoglcompat.dll) ซึ่งผู้เล่นรายงานว่าใช้ได้กับการ์ดจอ RX 500, RX 6000 และ RX 7600 และกราฟิก Ryzen บนเดสก์ท็อป",
			GpuCompat.Choice.Fluphus => "fluphus's AMD layer (App\\opengl32.dll), which holds the game at 60 FPS. In AMD Software set Wait for Vertical Refresh to Off, Unless Application Specifies, and turn off overlays and other frame limiters." + (his ? " No graphics card can be picked while it is on." : ""),
			_ => GpuCompat.HasNvidiaAdapter() ? "ปิดอยู่ มีการ์ดจอ NVIDIA ในเครื่อง: เลเยอร์ใช้สำหรับกราฟิก AMD และ Intel" : "ปิดอยู่ บนกราฟิก AMD และ Intel ให้เลือกเลเยอร์ ค่าเริ่มต้นคือของ Cloud23333"
		};
	}

	private void ResetDamageUi_OnClick(object sender, RoutedEventArgs e)
	{
		Slider damageNumberScaleSlider = DamageNumberScaleSlider;
		double value = (DamageTextureScaleSlider.Value = 100.0);
		damageNumberScaleSlider.Value = value;
		Slider damageNumberOpacitySlider = DamageNumberOpacitySlider;
		value = (DamageTextureOpacitySlider.Value = 100.0);
		damageNumberOpacitySlider.Value = value;
	}
}
