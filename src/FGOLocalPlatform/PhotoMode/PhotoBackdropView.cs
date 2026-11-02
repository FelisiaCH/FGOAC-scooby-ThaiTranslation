using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace FGOLocalPlatform;

public sealed class PhotoBackdropView : StackPanel, IDisposable
{
	private MemoryMappedFile? map;

	private MemoryMappedViewAccessor? ipc;

	private int? pid;

	private int sequence;

	private readonly CheckBox enabled = new CheckBox
	{
		Content = "พื้นหลังสีล้วน (คงตัวละครและเอฟเฟกต์ไว้)",
		Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
	};

	private readonly Slider[] rgb = new Slider[3];

	private readonly Border preview = new Border
	{
		Height = 24.0,
		Margin = new Thickness(0.0, 6.0, 0.0, 6.0)
	};

	private readonly TextBlock status = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap
	};

	public PhotoBackdropView()
	{
		base.Children.Add(new TextBlock
		{
			Text = "กรีนสกรีน",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 14.0, 0.0, 6.0)
		});
		base.Children.Add(enabled);
		base.Children.Add(new TextBlock
		{
			Text = "คงตัวละคร อาวุธ และเอฟเฟกต์อนุภาคไว้บนสีเดียว ส่วนฉากและหน้าจอของเกมจะถูกตัดออก กำหนดสีด้วยค่าแดง เขียว และน้ำเงิน",
			TextWrapping = TextWrapping.Wrap
		});
		for (int i = 0; i < 3; i++)
		{
			Grid grid = new Grid
			{
				Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
			};
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(76.0)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition());
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(88.0)
			});
			grid.Children.Add(new TextBlock
			{
				Text = (new string[3] { "แดง", "เขียว", "น้ำเงิน" })[i]
			});
			Slider slider = new Slider
			{
				Minimum = 0.0,
				Maximum = 255.0,
				Value = ((i == 1) ? 255 : 0),
				SmallChange = 1.0,
				TickFrequency = 1.0,
				IsSnapToTickEnabled = true
			};
			AutomationProperties.SetName(slider, "พื้นหลังสี" + (new string[3] { "แดง", "เขียว", "น้ำเงิน" })[i]);
			rgb[i] = slider;
			Grid.SetColumn(slider, 1);
			grid.Children.Add(slider);
			TextBox element = PhotoNumberInput.Create(slider);
			Grid.SetColumn(element, 2);
			grid.Children.Add(element);
			base.Children.Add(grid);
			slider.ValueChanged += delegate
			{
				Write();
			};
		}
		base.Children.Add(preview);
		Button button = new Button
		{
			Content = "รีเซ็ตเป็นสีเขียว",
			Margin = new Thickness(0.0, 4.0, 0.0, 8.0)
		};
		button.Click += delegate
		{
			Slider obj = rgb[0];
			double value = (rgb[2].Value = 0.0);
			obj.Value = value;
			rgb[1].Value = 255.0;
			Write();
		};
		base.Children.Add(button);
		base.Children.Add(status);
		enabled.Checked += delegate
		{
			Write();
		};
		enabled.Unchecked += delegate
		{
			Write();
		};
		Write();
	}

	public void Refresh(int? gamePid, bool active)
	{
		if (pid != gamePid)
		{
			Dispose();
			pid = gamePid;
		}
		base.IsEnabled = active;
		if (!active)
		{
			if (ipc != null)
			{
				ipc.Write(12L, 0);
			}
			return;
		}
		try
		{
			if (ipc == null && gamePid.HasValue)
			{
				map = MemoryMappedFile.CreateOrOpen($"Local\\FGOLocalPhotoBackdrop1_{gamePid}", 36L);
				ipc = map.CreateViewAccessor(0L, 36L);
				ipc.Write(0L, 1111969862);
				ipc.Write(4L, 1);
			}
			Write();
			int num = ipc?.ReadInt32(28L) ?? 0;
			status.Text = ((enabled.IsChecked != true) ? "ปิดพื้นหลังสีล้วนอยู่" : ((num == 1) ? $"เปิดพื้นหลังสีล้วนอยู่ เฟรมนี้คงการวาดตัวละครและเอฟเฟกต์ไว้ {ipc.ReadInt32(32L)} รายการ" : ((num < 0) ? "ตั้งค่าพื้นหลังสีล้วนไม่ได้ จึงคงภาพเดิมไว้" : "กำลังรอให้ตัวละครและเอฟเฟกต์วาดภาพ")));
		}
		catch (IOException)
		{
			status.Text = "ไม่มีการเชื่อมต่อของพื้นหลังสีล้วน";
		}
	}

	private void Write()
	{
		if (rgb.Any((Slider x) => x == null))
		{
			return;
		}
		preview.Background = new SolidColorBrush(Color.FromRgb((byte)rgb[0].Value, (byte)rgb[1].Value, (byte)rgb[2].Value));
		if (ipc != null && base.IsEnabled)
		{
			ipc.Write(8L, ++sequence);
			ipc.Write(12L, (enabled.IsChecked == true) ? 1 : 0);
			for (int num = 0; num < 3; num++)
			{
				ipc.Write(16 + num * 4, (float)(rgb[num].Value / 255.0));
			}
			ipc.Write(8L, ++sequence);
		}
	}

	public void Load(JsonNode? photo)
	{
		if (!(photo?["backdropRgb"] is JsonArray { Count: 3 } jsonArray))
		{
			return;
		}
		for (int i = 0; i < 3; i++)
		{
			if (jsonArray[i] is JsonValue jsonValue && jsonValue.TryGetValue<double>(out var value) && double.IsFinite(value))
			{
				rgb[i].Value = Math.Clamp(value, 0.0, 255.0);
			}
		}
	}

	public void Save(JsonObject photo)
	{
		JsonNode[] items = rgb.Select((Slider x) => JsonValue.Create((int)x.Value)).ToArray();
		photo["backdropRgb"] = new JsonArray(items);
	}

	public void Dispose()
	{
		if (ipc != null)
		{
			ipc.Write(12L, 0);
		}
		ipc?.Dispose();
		map?.Dispose();
		ipc = null;
		map = null;
	}
}
