using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FGOLocalPlatform;

public partial class CursorSettingsView : UserControl, IComponentConnector
{
	public sealed class Preferences
	{
		public int Size { get; set; } = 48;

		public int Outline { get; set; } = 2;

		public double X { get; set; } = 50.0;

		public double Y { get; set; } = 50.0;
	}

	private readonly string directory = Path.Combine(GamePaths.GameRoot, "cursor");

	private readonly string ini = Path.Combine(GamePaths.GameRoot, "segatools.ini");

	private BitmapSource? artwork;

	private bool ready;

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern uint GetPrivateProfileInt(string section, string key, int fallback, string file);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool WritePrivateProfileString(string section, string key, string value, string file);

	public CursorSettingsView()
	{
		InitializeComponent();
		try
		{
			string path = Path.Combine(directory, "settings.json");
			Preferences preferences = (File.Exists(path) ? (JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? new Preferences()) : new Preferences());
			SizeSlider.Value = preferences.Size;
			OutlineSlider.Value = preferences.Outline;
			HotX.Value = preferences.X;
			HotY.Value = preferences.Y;
			string path2 = Path.Combine(directory, "source.png");
			if (File.Exists(path2))
			{
				artwork = CursorArtwork.Load(path2);
				ImageName.Text = "ภาพที่กำหนดเองซึ่งบันทึกไว้";
			}
		}
		catch (Exception ex)
		{
			Status.Text = "อ่านการตั้งค่าตัวชี้ที่บันทึกไว้ไม่ได้: " + ex.Message;
		}
		Mode.SelectedIndex = Math.Clamp((int)GetPrivateProfileInt("touch", "cursorStyle", 0, ini), 0, 2);
		try
		{
			string text = Path.Combine(directory, "custom.cur");
			if (Mode.SelectedIndex == 2 && artwork != null && File.Exists(text))
			{
				byte[] array = File.ReadAllBytes(text);
				if (array.Length >= 38 && BitConverter.ToUInt16(array, 2) == 2 && BitConverter.ToUInt16(array, 36) == 24)
				{
					string text2 = text + ".24bpp.bak";
					if (!File.Exists(text2))
					{
						File.Copy(text, text2);
					}
					byte[] bytes = CursorArtwork.Build(artwork, (int)SizeSlider.Value, (int)OutlineSlider.Value, HotX.Value / 100.0, HotY.Value / 100.0);
					AtomicFile.WriteAllBytes(text, bytes);
					Status.Text = "อัปเกรดรูปแบบสีตัวชี้แบบเก่าแล้ว - จะมีผลเมื่อเริ่มเกมครั้งถัดไป";
				}
			}
		}
		catch (Exception ex2)
		{
			Status.Text = "อัปเกรดรูปแบบตัวชี้ไม่ได้ กรุณาบันทึกอีกครั้ง: " + ex2.Message;
		}
		ready = true;
		RefreshPreview();
	}

	private void Choose_OnClick(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "รูปภาพ|*.png;*.jpg;*.jpeg;*.bmp",
			Title = "เลือกภาพตัวชี้"
		};
		if (openFileDialog.ShowDialog() != true)
		{
			return;
		}
		try
		{
			artwork = CursorArtwork.Load(openFileDialog.FileName);
			ImageName.Text = Path.GetFileName(openFileDialog.FileName);
			Mode.SelectedIndex = 2;
			RefreshPreview();
		}
		catch (Exception ex)
		{
			Status.Text = "เปิดภาพไม่ได้: " + ex.Message;
		}
	}

	private void Settings_OnChanged(object sender, RoutedEventArgs e)
	{
		if (ready)
		{
			RefreshPreview();
			Status.Text = "ยังไม่ได้บันทึกการตั้งค่า";
		}
	}

	private void RefreshPreview()
	{
		Hotspot.Visibility = ((artwork == null || Mode.SelectedIndex != 2) ? Visibility.Collapsed : Visibility.Visible);
		if (artwork == null || Mode.SelectedIndex != 2)
		{
			Preview.Source = null;
			return;
		}
		int num = (int)SizeSlider.Value;
		Preview.Source = CursorArtwork.Preview(artwork, num, (int)OutlineSlider.Value);
		Canvas.SetLeft(Hotspot, (double)(130 - num) / 2.0 + HotX.Value / 100.0 * (double)(num - 1) - 2.5);
		Canvas.SetTop(Hotspot, (double)(130 - num) / 2.0 + HotY.Value / 100.0 * (double)(num - 1) - 2.5);
	}

	private void Save_OnClick(object sender, RoutedEventArgs e)
	{
		try
		{
			string text = "";
			if (Mode.SelectedIndex == 2)
			{
				if (artwork == null)
				{
					throw new InvalidOperationException("Choose a pointer image first.");
				}
				byte[] bytes = CursorArtwork.Build(artwork, (int)SizeSlider.Value, (int)OutlineSlider.Value, HotX.Value / 100.0, HotY.Value / 100.0);
				Directory.CreateDirectory(directory);
				text = "cursor\\custom.cur";
				File.WriteAllBytes(Path.Combine(GamePaths.GameRoot, text), bytes);
				PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
				pngBitmapEncoder.Frames.Add(BitmapFrame.Create(artwork));
				using (FileStream stream = File.Create(Path.Combine(directory, "source.png")))
				{
					pngBitmapEncoder.Save(stream);
				}
				File.WriteAllText(Path.Combine(directory, "settings.json"), JsonSerializer.Serialize(new Preferences
				{
					Size = (int)SizeSlider.Value,
					Outline = (int)OutlineSlider.Value,
					X = HotX.Value,
					Y = HotY.Value
				}));
			}
			(string, string)[] array = new(string, string)[4]
			{
				("cursorFile", text),
				("cursorSize", ((int)SizeSlider.Value).ToString()),
				("cursorStyle", Mode.SelectedIndex.ToString()),
				("cursor", "1")
			};
			for (int i = 0; i < array.Length; i++)
			{
				(string, string) tuple = array[i];
				if (!WritePrivateProfileString("touch", tuple.Item1, tuple.Item2, ini))
				{
					throw new Win32Exception(Marshal.GetLastWin32Error());
				}
			}
			Status.Text = "บันทึกการตั้งค่าตัวชี้แล้ว - จะมีผลเมื่อเริ่มเกมครั้งถัดไป";
		}
		catch (Exception ex)
		{
			Status.Text = "บันทึกไม่สำเร็จ: " + ex.Message;
		}
	}
}
