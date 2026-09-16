using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FGOLocalPlatform;

public sealed class PhotoModelView : StackPanel, IDisposable
{
	private sealed record Saved(double[] Offset, double[] Rotation, bool Weapons, bool Head, bool ManualHead, double[] HeadAngles, int[] Modes, int[] States);

	private readonly PhotoFaceView selection;

	private readonly Slider[] offsets = new Slider[3];

	private readonly Slider[] rotations = new Slider[3];

	private readonly Slider[] headAngles = new Slider[3];

	private readonly CheckBox manualHead = new CheckBox
	{
		Content = "หมุนศีรษะเอง",
		Margin = new Thickness(0.0, 10.0, 0.0, 8.0)
	};

	private readonly CheckBox weapons = new CheckBox
	{
		Content = "แสดงอาวุธ",
		Foreground = Brushes.White,
		IsChecked = true,
		Margin = new Thickness(0.0, 10.0, 0.0, 10.0)
	};

	private readonly CheckBox head = new CheckBox
	{
		Content = "ให้ศีรษะหันตามกล้อง",
		Foreground = Brushes.White,
		Margin = new Thickness(0.0, 10.0, 0.0, 10.0)
	};

	private readonly ComboBox weaponChoice = new ComboBox
	{
		Margin = new Thickness(0.0, 6.0, 0.0, 6.0),
		MaxDropDownHeight = 220.0
	};

	private readonly ComboBox weaponPosition = new ComboBox
	{
		Margin = new Thickness(0.0, 6.0, 0.0, 6.0),
		MaxDropDownHeight = 220.0
	};

	private readonly ComboBox weaponMode = new ComboBox
	{
		Margin = new Thickness(0.0, 6.0, 0.0, 6.0)
	};

	private readonly int[] weaponModes = new int[10];

	private readonly int[] weaponStates = new int[10];

	private bool syncingWeapons;

	private bool loadingActor;

	private readonly Dictionary<ulong, Saved> saved = new Dictionary<ulong, Saved>();

	private string weaponSignature = "";

	private int weaponMask = -1;

	private readonly TextBlock status = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap
	};

	private MemoryMappedFile? mapping;

	private MemoryMappedViewAccessor? ipc;

	private int? pid;

	private ulong actor;

	private bool dirty = true;

	private static int Choice(ComboBox box)
	{
		if (box.SelectedItem is ComboBoxItem { Tag: var tag } && tag is int)
		{
			return (int)tag;
		}
		return 0;
	}

	public PhotoModelView(PhotoFaceView selection)
	{
		this.selection = selection;
		base.Children.Add(new TextBlock
		{
			Text = "โมเดลและอาวุธ",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		});
		base.Children.Add(new TextBlock
		{
			Text = "เลื่อนตัวละครที่เลือกไว้ในอนิเมชันใบหน้าด้านบนไปตามแกนของฉาก กล้องจะไม่ขยับ",
			TextWrapping = TextWrapping.Wrap
		});
		for (int i = 0; i < 3; i++)
		{
			Grid grid = new Grid
			{
				Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
			};
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(65.0)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition());
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(90.0)
			});
			grid.Children.Add(new TextBlock
			{
				Text = (new string[3] { "X แนวข้าง", "Y ความสูง", "Z แนวลึก" })[i]
			});
			Slider slider = new Slider
			{
				Minimum = -20.0,
				Maximum = 20.0,
				SmallChange = 0.01,
				LargeChange = 0.1
			};
			offsets[i] = slider;
			Grid.SetColumn(slider, 1);
			grid.Children.Add(slider);
			TextBox element = PhotoNumberInput.Create(slider);
			Grid.SetColumn(element, 2);
			grid.Children.Add(element);
			slider.ValueChanged += delegate
			{
				dirty = true;
				Publish();
			};
			base.Children.Add(grid);
		}
		Button button = new Button
		{
			Content = "รีเซ็ตตำแหน่ง",
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		button.Click += delegate
		{
			Slider[] array2 = offsets;
			for (int j = 0; j < array2.Length; j++)
			{
				array2[j].Value = 0.0;
			}
			dirty = true;
			Publish();
		};
		base.Children.Add(button);
		base.Children.Add(new TextBlock
		{
			Text = "การหมุนโมเดล (องศา)",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 10.0, 0.0, 4.0)
		});
		base.Children.Add(new TextBlock
		{
			Text = "หมุนโมเดลรอบตำแหน่งของตัวเองตามแกน X, Y และ Z ของฉาก โดย 0° คือทิศทางเดิม",
			TextWrapping = TextWrapping.Wrap
		});
		for (int num = 0; num < 3; num++)
		{
			Grid grid2 = new Grid
			{
				Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
			};
			grid2.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(65.0)
			});
			grid2.ColumnDefinitions.Add(new ColumnDefinition());
			grid2.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(90.0)
			});
			grid2.Children.Add(new TextBlock
			{
				Text = (new string[3] { "X ก้มเงย", "Y ส่ายซ้ายขวา", "Z เอียงข้าง" })[num],
				VerticalAlignment = VerticalAlignment.Center
			});
			Slider slider2 = new Slider
			{
				Minimum = -180.0,
				Maximum = 180.0,
				SmallChange = 0.1,
				LargeChange = 5.0
			};
			rotations[num] = slider2;
			Grid.SetColumn(slider2, 1);
			grid2.Children.Add(slider2);
			TextBox element2 = PhotoNumberInput.Create(slider2);
			Grid.SetColumn(element2, 2);
			grid2.Children.Add(element2);
			slider2.ValueChanged += delegate
			{
				dirty = true;
				Publish();
			};
			base.Children.Add(grid2);
		}
		Button button2 = new Button
		{
			Content = "รีเซ็ตการหมุน",
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		button2.Click += delegate
		{
			Slider[] array2 = rotations;
			for (int j = 0; j < array2.Length; j++)
			{
				array2[j].Value = 0.0;
			}
			dirty = true;
			Publish();
		};
		base.Children.Add(button2);
		base.Children.Add(weapons);
		base.Children.Add(new TextBlock
		{
			Text = "แสดงอาวุธที่ตั้งค่าไว้ด้านล่าง ปิดตัวเลือกนี้เพื่อซ่อนทั้งหมด",
			TextWrapping = TextWrapping.Wrap
		});
		base.Children.Add(status);
		weapons.Checked += delegate
		{
			dirty = true;
			Publish();
		};
		weapons.Unchecked += delegate
		{
			dirty = true;
			Publish();
		};
		base.Children.Add(head);
		base.Children.Add(new TextBlock
		{
			Text = "หมุนศีรษะซ้อนทับท่าทางปัจจุบัน และค่อย ๆ หันกลับมาตรงกลางเมื่อกล้องเลยขีดจำกัดของตัวละคร ดวงตาจะไม่ได้รับผลกระทบ",
			TextWrapping = TextWrapping.Wrap
		});
		head.Checked += delegate
		{
			dirty = true;
			Publish();
		};
		head.Unchecked += delegate
		{
			dirty = true;
			Publish();
		};
		base.Children.Add(manualHead);
		base.Children.Add(new TextBlock
		{
			Text = "ใช้ขีดจำกัดและการหน่วงชุดเดียวกับให้ศีรษะหันตามกล้อง เมื่อเปิดการหันตาม ค่าเหล่านี้ใช้ปรับละเอียดบนแกน X, Y และ Z เฉพาะที่ เมื่อปิดการหันตาม ค่าเหล่านี้จะกำหนดทิศศีรษะเอง ค่าทั้งหมดเป็นองศา และ 0° จะไม่เพิ่มค่าชดเชยใด ๆ",
			TextWrapping = TextWrapping.Wrap
		});
		manualHead.Checked += delegate
		{
			dirty = true;
			Publish();
		};
		manualHead.Unchecked += delegate
		{
			dirty = true;
			Publish();
		};
		for (int num2 = 0; num2 < 3; num2++)
		{
			Grid grid3 = new Grid
			{
				Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
			};
			grid3.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(65.0)
			});
			grid3.ColumnDefinitions.Add(new ColumnDefinition());
			grid3.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(90.0)
			});
			grid3.Children.Add(new TextBlock
			{
				Text = (new string[3] { "ศีรษะ X", "ศีรษะ Y", "ศีรษะ Z" })[num2],
				VerticalAlignment = VerticalAlignment.Center
			});
			Slider slider3 = new Slider
			{
				Minimum = -180.0,
				Maximum = 180.0,
				SmallChange = 0.1,
				LargeChange = 5.0
			};
			headAngles[num2] = slider3;
			Grid.SetColumn(slider3, 1);
			grid3.Children.Add(slider3);
			TextBox element3 = PhotoNumberInput.Create(slider3);
			Grid.SetColumn(element3, 2);
			grid3.Children.Add(element3);
			slider3.ValueChanged += delegate
			{
				dirty = true;
				Publish();
			};
			base.Children.Add(grid3);
		}
		Button button3 = new Button
		{
			Content = "รีเซ็ตการหมุนศีรษะ",
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		button3.Click += delegate
		{
			Slider[] array2 = headAngles;
			for (int j = 0; j < array2.Length; j++)
			{
				array2[j].Value = 0.0;
			}
			dirty = true;
			Publish();
		};
		base.Children.Add(button3);
		base.Children.Add(new TextBlock
		{
			Text = "การสลับอาวุธ",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 14.0, 0.0, 0.0)
		});
		weaponChoice.Items.Add(new ComboBoxItem
		{
			Content = "เลือกอาวุธที่ต้องการปรับ",
			Tag = 0
		});
		weaponChoice.SelectedIndex = 0;
		weaponPosition.Items.Add(new ComboBoxItem
		{
			Content = "จุดติดตั้งเดิม",
			Tag = 0
		});
		weaponPosition.SelectedIndex = 0;
		string[] array = new string[3] { "คงไว้ตามเดิม", "แสดง", "ซ่อน" };
		foreach (string newItem in array)
		{
			weaponMode.Items.Add(newItem);
		}
		weaponMode.SelectedIndex = 0;
		weaponMode.IsEnabled = false;
		weaponPosition.IsEnabled = false;
		base.Children.Add(weaponChoice);
		base.Children.Add(weaponMode);
		base.Children.Add(weaponPosition);
		Button button4 = new Button
		{
			Content = "คืนค่าอาวุธเดิม",
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		button4.Click += delegate
		{
			Array.Clear(weaponModes);
			Array.Clear(weaponStates);
			weapons.IsChecked = true;
			LoadWeaponEditor();
			dirty = true;
			Publish();
		};
		base.Children.Add(button4);
		base.Children.Add(new TextBlock
		{
			Text = "อาวุธแต่ละชิ้นตั้งค่าแยกกัน และแสดงพร้อมกันหลายชิ้นได้ เมื่อออกจากโหมดถ่ายภาพจะคืนค่าเดิมทั้งหมด",
			TextWrapping = TextWrapping.Wrap
		});
		weaponChoice.SelectionChanged += delegate
		{
			if (!syncingWeapons)
			{
				LoadWeaponEditor();
				dirty = true;
				Publish();
			}
		};
		weaponMode.SelectionChanged += delegate
		{
			int num4 = Choice(weaponChoice);
			if (!syncingWeapons && num4 > 0)
			{
				weaponModes[num4 - 1] = weaponMode.SelectedIndex;
				if (weaponMode.SelectedIndex == 1)
				{
					weapons.IsChecked = true;
				}
				dirty = true;
				Publish();
			}
		};
		weaponPosition.SelectionChanged += delegate
		{
			int num4 = Choice(weaponChoice);
			if (!syncingWeapons && num4 > 0)
			{
				weaponStates[num4 - 1] = Choice(weaponPosition);
				weaponModes[num4 - 1] = 1;
				weaponMode.SelectedIndex = 1;
				weapons.IsChecked = true;
				dirty = true;
				Publish();
			}
		};
	}

	public void Refresh(int? gamePid, bool active)
	{
		if (pid != gamePid)
		{
			Dispose();
			pid = gamePid;
		}
		base.IsEnabled = active && selection.SelectedActorId != 0;
		if (!active)
		{
			saved.Clear();
			actor = 0uL;
			Array.Clear(weaponModes);
			Array.Clear(weaponStates);
			Slider[] array = offsets;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Value = 0.0;
			}
			array = rotations;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Value = 0.0;
			}
			weapons.IsChecked = true;
			head.IsChecked = false;
			manualHead.IsChecked = false;
			array = headAngles;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Value = 0.0;
			}
			dirty = true;
			status.Text = "เข้าโหมดถ่ายภาพ แล้วเลือกตัวละคร";
			return;
		}
		try
		{
			if (ipc == null && gamePid.HasValue)
			{
				mapping = MemoryMappedFile.OpenExisting($"Local\\FGOLocalPhotoModel_{gamePid}");
				ipc = mapping.CreateViewAccessor(0L, 856L);
				if (ipc.ReadInt32(0L) != 1297106758 || ipc.ReadInt32(4L) != 6)
				{
					Dispose();
					status.Text = "เริ่มเกมใหม่เพื่อโหลดฮุกโหมดถ่ายภาพรุ่นใหม่กว่า";
					return;
				}
			}
			if (actor != selection.SelectedActorId)
			{
				if (ipc != null && ipc.ReadInt32(8L) != 0)
				{
					return;
				}
				if (actor != 0L)
				{
					saved[actor] = new Saved(offsets.Select((Slider s) => s.Value).ToArray(), rotations.Select((Slider s) => s.Value).ToArray(), weapons.IsChecked == true, head.IsChecked == true, manualHead.IsChecked == true, headAngles.Select((Slider s) => s.Value).ToArray(), (int[])weaponModes.Clone(), (int[])weaponStates.Clone());
				}
				loadingActor = true;
				try
				{
					actor = selection.SelectedActorId;
					saved.TryGetValue(actor, out Saved value);
					for (int num = 0; num < 3; num++)
					{
						offsets[num].Value = (((object)value != null) ? value.Offset[num] : 0.0);
						rotations[num].Value = (((object)value != null) ? value.Rotation[num] : 0.0);
					}
					for (int num2 = 0; num2 < 10; num2++)
					{
						weaponModes[num2] = (((object)value != null) ? value.Modes[num2] : 0);
						weaponStates[num2] = (((object)value != null) ? value.States[num2] : 0);
					}
					weapons.IsChecked = value?.Weapons ?? true;
					head.IsChecked = value?.Head ?? false;
					manualHead.IsChecked = value?.ManualHead ?? false;
					for (int num3 = 0; num3 < 3; num3++)
					{
						headAngles[num3].Value = (((object)value != null) ? value.HeadAngles[num3] : 0.0);
					}
					weaponChoice.SelectedIndex = 0;
					LoadWeaponEditor();
					weaponSignature = "";
					dirty = true;
				}
				finally
				{
					loadingActor = false;
				}
			}
			Publish();
			RefreshWeapons();
			TextBlock textBlock = status;
			textBlock.Text = ipc?.ReadInt32(12L) switch
			{
				1 => (weapons.IsChecked == true) ? "กำลังแสดงอาวุธ" : "ซ่อนอาวุธอยู่ - ติ๊กแสดงอาวุธด้านบน", 
				-1 => "ตัวละครนั้นออกจากฉากไปแล้ว", 
				-2 => "ค่าเหล่านั้นไม่ถูกต้อง", 
				-3 => "ตอนนี้แก้ไขโมเดลนี้ไม่ได้", 
				-5 => "อาวุธชิ้นนี้ไม่มีจุดติดตั้งเดิม - เลือกตำแหน่งจากรายการ", 
				_ => "เลือกตัวละคร", 
			};
			if (head.IsChecked == true || manualHead.IsChecked == true)
			{
				MemoryMappedViewAccessor? memoryMappedViewAccessor = ipc;
				if (memoryMappedViewAccessor != null && memoryMappedViewAccessor.ReadInt32(44L) == -4)
				{
					status.Text = "ไม่พบกระดูกศีรษะหรือขีดจำกัดการหันตามที่ใช้ได้บนตัวละครนี้";
				}
			}
		}
		catch (FileNotFoundException)
		{
			status.Text = "ต้องใช้ฮุกเกมรุ่นใหม่กว่า - เริ่มเกมใหม่";
		}
		catch (IOException)
		{
			Dispose();
			status.Text = "การเชื่อมต่อกับเกมขาดหาย";
		}
	}

	private void Publish()
	{
		if (!loadingActor && base.IsEnabled && dirty && ipc != null && ipc.ReadInt32(8L) == 0)
		{
			ipc.Write(16L, actor);
			for (int i = 0; i < 3; i++)
			{
				ipc.Write(24 + i * 4, (float)offsets[i].Value);
			}
			for (int j = 0; j < 3; j++)
			{
				ipc.Write(824 + j * 4, (float)rotations[j].Value);
			}
			ipc.Write(36L, (weapons.IsChecked != true) ? 1 : 0);
			ipc.Write(40L, (head.IsChecked == true) ? 1 : 0);
			ipc.Write(836L, (manualHead.IsChecked == true) ? 1 : 0);
			for (int k = 0; k < 3; k++)
			{
				ipc.Write(840 + k * 4, (float)headAngles[k].Value);
			}
			ipc.Write(48L, Choice(weaponChoice));
			ipc.Write(52L, 0);
			ipc.WriteArray(704L, weaponModes, 0, 10);
			ipc.WriteArray(744L, weaponStates, 0, 10);
			ipc.Write(8L, 1);
			dirty = false;
		}
	}

	private void LoadWeaponEditor()
	{
		syncingWeapons = true;
		try
		{
			int num = Choice(weaponChoice);
			ComboBox comboBox = weaponMode;
			bool isEnabled = (weaponPosition.IsEnabled = num > 0);
			comboBox.IsEnabled = isEnabled;
			weaponMode.SelectedIndex = ((num > 0) ? weaponModes[num - 1] : 0);
			weaponPosition.Items.Clear();
			weaponPosition.Items.Add(new ComboBoxItem
			{
				Content = "จุดติดตั้งเดิม",
				Tag = 0
			});
			weaponPosition.SelectedIndex = 0;
			weaponMask = -1;
		}
		finally
		{
			syncingWeapons = false;
		}
	}

	private void RefreshWeapons()
	{
		if (ipc == null || ipc.ReadInt32(8L) != 0)
		{
			return;
		}
		int num = ipc.ReadInt32(60L);
		if (num < 0 || num > 10)
		{
			return;
		}
		List<string> list = new List<string>();
		for (int i = 0; i < num; i++)
		{
			byte[] array = new byte[64];
			ipc.ReadArray(64 + i * 64, array, 0, 64);
			int num2 = Array.IndexOf(array, (byte)0);
			list.Add(Encoding.UTF8.GetString(array, 0, (num2 < 0) ? 64 : num2));
		}
		string text = string.Join("\n", list);
		int num3 = ipc.ReadInt32(56L);
		syncingWeapons = true;
		try
		{
			if (text != weaponSignature)
			{
				int num4 = Choice(weaponChoice);
				weaponChoice.Items.Clear();
				weaponChoice.Items.Add(new ComboBoxItem
				{
					Content = "เลือกอาวุธที่ต้องการปรับ",
					Tag = 0
				});
				for (int j = 0; j < list.Count; j++)
				{
					weaponChoice.Items.Add(new ComboBoxItem
					{
						Content = $"{j + 1} - {list[j]}",
						Tag = j + 1
					});
				}
				weaponChoice.SelectedIndex = ((num4 <= num) ? num4 : 0);
				weaponSignature = text;
				if (num4 > num)
				{
					dirty = true;
					weaponMask = -1;
				}
			}
			if (num3 == weaponMask)
			{
				return;
			}
			int num5 = Choice(weaponChoice);
			int num6 = ((num5 > 0) ? weaponStates[num5 - 1] : 0);
			weaponPosition.Items.Clear();
			weaponPosition.Items.Add(new ComboBoxItem
			{
				Content = "จุดติดตั้งเดิม",
				Tag = 0
			});
			string[] array2 = new string[14]
			{
				"", "มือขวา", "มือซ้าย", "", "ลำตัวบน", "หน้าอก", "ต้นขาซ้าย", "ลำตัวล่าง", "ศีรษะ", "เอว",
				"ปลายแขนขวา", "ปลายแขนซ้าย", "จุดราก", "ฐาน"
			};
			for (int k = 1; k < array2.Length; k++)
			{
				if ((num3 & (1 << k)) != 0)
				{
					weaponPosition.Items.Add(new ComboBoxItem
					{
						Content = array2[k],
						Tag = k
					});
				}
			}
			weaponPosition.SelectedIndex = 0;
			foreach (ComboBoxItem item in (IEnumerable)weaponPosition.Items)
			{
				if ((int)item.Tag == num6)
				{
					weaponPosition.SelectedItem = item;
				}
			}
			if (Choice(weaponPosition) != num6 && num5 > 0)
			{
				weaponStates[num5 - 1] = 0;
				dirty = true;
			}
			weaponMask = num3;
		}
		finally
		{
			syncingWeapons = false;
		}
	}

	public void Dispose()
	{
		saved.Clear();
		ipc?.Dispose();
		mapping?.Dispose();
		ipc = null;
		mapping = null;
		actor = 0uL;
		dirty = true;
	}
}
