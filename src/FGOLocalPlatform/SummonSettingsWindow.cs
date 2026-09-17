using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;

namespace FGOLocalPlatform;

public partial class SummonSettingsWindow : UserControl, IComponentConnector
{
	private readonly string settingsPath;

	private readonly List<SummonCardOption> cards = new List<SummonCardOption>();

	private ICollectionView? view;

	private bool batching;

	private bool dirty;

	private string? loadedText;

	public SummonSettingsWindow(string appRoot, string? configurationPath = null)
	{
		InitializeComponent();
		string fullPath = Path.GetFullPath(Path.Combine(appRoot, "..", "Server", "artemis"));
		settingsPath = configurationPath ?? Path.Combine(fullPath, "config", "fgo_summon_weights.json");
		JsonObject jsonObject = JsonNode.Parse(File.ReadAllText(Path.Combine(appRoot, "deck.json"))).AsObject();
		string fullPath2 = Path.GetFullPath(Path.Combine(appRoot, jsonObject["CardsPath"].GetValue<string>()));
		foreach (JsonNode item in JsonNode.Parse(File.ReadAllText(Path.Combine(fullPath, "titles", "fgo", "data", "summon_candidates.json")))["cards"].AsArray())
		{
			SummonCardOption summonCardOption = new SummonCardOption
			{
				TcId = item["tc_id"].GetValue<int>(),
				Kind = item["type"].GetValue<int>(),
				EntityId = item["entity_id"].GetValue<int>(),
				Name = item["name"].GetValue<string>(),
				Rarity = item["rarity"].GetValue<int>(),
				Category = (item["category"]?.GetValue<string>() ?? "regular"),
				HoloType = (item["holo_type"]?.GetValue<int>() ?? 0),
				AcquisitionNote = string.Join("\n", (from n in item["acquisition_notes"]?.AsArray()
					select n.GetValue<string>()) ?? Enumerable.Empty<string>()),
				ImagePath = Path.Combine(fullPath2, Path.GetFileName(item["file_name"].GetValue<string>()))
			};
			cards.Add(summonCardOption);
			summonCardOption.PropertyChanged += delegate(object? _, PropertyChangedEventArgs e)
			{
				if (e.PropertyName == "WeightText" && !batching)
				{
					dirty = true;
					Recalculate();
				}
			};
		}
		view = CollectionViewSource.GetDefaultView(cards);
		view.Filter = Filter;
		CardsGrid.ItemsSource = (IEnumerable)view;
		LoadSettings();
		try
		{
			RefreshPresetList();
		}
		catch (Exception ex)
		{
			StatusText.Text = "แสดงรายการพรีเซ็ตไม่ได้: " + ex.Message;
		}
	}

	private bool Filter(object value)
	{
		SummonCardOption summonCardOption = (SummonCardOption)value;
		string text = SearchBox.Text.Trim();
		if (CategoryFilter.SelectedIndex switch
		{
			1 => summonCardOption.Category == "regular", 
			2 => summonCardOption.Category == "supplemental", 
			3 => summonCardOption.IsStory, 
			_ => !summonCardOption.IsStory, 
		} && (KindFilter.SelectedIndex == 0 || summonCardOption.Kind == KindFilter.SelectedIndex))
		{
			if (text.Length != 0 && !summonCardOption.Name.Contains(text, StringComparison.OrdinalIgnoreCase) && !summonCardOption.EnglishName.Contains(text, StringComparison.OrdinalIgnoreCase) && !summonCardOption.FileNumber.Contains(text, StringComparison.OrdinalIgnoreCase) && !summonCardOption.FileName.Contains(text, StringComparison.OrdinalIgnoreCase))
			{
				return summonCardOption.TcId.ToString().Contains(text);
			}
			return true;
		}
		return false;
	}

	private void Search_OnChanged(object sender, TextChangedEventArgs e)
	{
		ICollectionView? obj = view;
		if (obj != null)
		{
			obj.Refresh();
		}
	}

	private void Filter_OnChanged(object sender, SelectionChangedEventArgs e)
	{
		ICollectionView? obj = view;
		if (obj != null)
		{
			obj.Refresh();
		}
		if (EditButtons != null && CategoryFilter != null)
		{
			EditButtons.IsEnabled = CategoryFilter.SelectedIndex != 3;
		}
	}

	private void Grid_OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
	{
		((DispatcherObject)this).Dispatcher.BeginInvoke((DispatcherPriority)4, (Delegate)new Action(Recalculate));
	}

	private void Grid_OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
	{
		if (e.Row.Item is SummonCardOption { IsStory: not false })
		{
			e.Cancel = true;
			StatusText.Text = "การ์ดที่กำหนดตายตัวจากเนื้อเรื่องเป็นแบบอ่านอย่างเดียว และเพิ่มเข้ากลุ่มสุ่มไม่ได้";
		}
	}

	private void Grid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (StatusText != null && CardsGrid.SelectedItem is SummonCardOption { IsStory: not false } summonCardOption)
		{
			StatusText.Text = summonCardOption.AcquisitionNote.Split('\n')[0];
		}
	}

	/// <summary>
	/// Fills the table from the text of a weights file and returns how many cards were left out.
	/// The live file is read strictly, because the server rejects a card id it cannot draw; a
	/// preset made on another install is read tolerantly, dropping the ids this game does not have.
	/// </summary>
	private int ApplyWeightsText(string? text, bool tolerant)
	{
		JsonObject jsonObject = ((text == null) ? null : JsonNode.Parse(text).AsObject());
		JsonObject jsonObject2 = null;
		int skipped = 0;
		if (jsonObject != null)
		{
			JsonNode? jsonNode = jsonObject["version"];
			if (jsonNode == null || jsonNode.GetValue<int>() != 1 || !(jsonObject["weights"] is JsonObject jsonObject3))
			{
				throw new InvalidDataException("เวอร์ชันหรือรูปแบบของไฟล์ตั้งค่าไม่ถูกต้อง");
			}
			jsonObject2 = jsonObject3;
			HashSet<string> hashSet = (from c in cards
				where !c.IsStory
				select c.TcId.ToString(CultureInfo.InvariantCulture)).ToHashSet();
			foreach (KeyValuePair<string, JsonNode> item in jsonObject2.ToList())
			{
				if (!hashSet.Contains(item.Key) && tolerant)
				{
					jsonObject2.Remove(item.Key);
					skipped++;
					continue;
				}
				if (!hashSet.Contains(item.Key) || !(item.Value is JsonValue jsonValue) || !jsonValue.TryGetValue<int>(out var value) || value < 0 || value > 1000000)
				{
					throw new InvalidDataException("การ์ด " + item.Key + " สุ่มไม่ได้ หรือค่าน้ำหนักไม่ถูกต้อง");
				}
			}
			if (!jsonObject2.Any<KeyValuePair<string, JsonNode>>((KeyValuePair<string, JsonNode> pair) => pair.Value.GetValue<int>() > 0))
			{
				throw new InvalidDataException("ต้องมีการ์ดอย่างน้อยหนึ่งใบที่มีค่าน้ำหนักมากกว่า 0");
			}
		}
		batching = true;
		foreach (SummonCardOption card in cards)
		{
			card.WeightText = ((!card.IsStory) ? ((jsonObject2 == null) ? 1 : (jsonObject2[card.TcId.ToString()]?.GetValue<int>() ?? 0)) : 0).ToString(CultureInfo.InvariantCulture);
		}
		batching = false;
		Recalculate();
		return skipped;
	}

	private void LoadSettings()
	{
		try
		{
			string text = (File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null);
			ApplyWeightsText(text, tolerant: false);
			loadedText = text;
			dirty = false;
			StatusText.Text = ((text == null) ? "ยังไม่ได้บันทึกอัตราที่กำหนดเอง - การ์ดที่สุ่มได้ทุกใบมีโอกาสเท่ากัน" : "โหลดอัตราที่บันทึกไว้แล้ว การสุ่มที่กำลังดำเนินอยู่และการสุ่มซ้ำของมันจะยังคงผลลัพธ์เดิม");
		}
		catch (Exception ex)
		{
			batching = false;
			loadedText = (File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null);
			dirty = true;
			Recalculate();
			StatusText.Text = "อ่านอัตราไม่ได้: " + ex.Message + " เซิร์ฟเวอร์จะปฏิเสธไฟล์ที่ไม่ถูกต้อง - แก้ไขค่าที่นี่แล้วบันทึก";
		}
	}

	private void Recalculate()
	{
		bool flag = cards.Count > 0 && cards.All((SummonCardOption c) => c.Valid);
		decimal num = ((IEnumerable<SummonCardOption>)cards).Sum((Func<SummonCardOption, decimal>)((SummonCardOption c) => c.Weight));
		foreach (SummonCardOption card in cards)
		{
			card.SetTotal(flag ? num : 0m);
		}
		SaveButton.IsEnabled = flag && num > 0m;
		decimal num2 = cards.Where((SummonCardOption c) => c.Kind == 1).Sum((Func<SummonCardOption, decimal>)((SummonCardOption c) => c.Weight));
		SummaryText.Text = ((!flag) ? "กรอกค่าน้ำหนักแต่ละค่าเป็นจำนวนเต็มตั้งแต่ 0 ถึง 1,000,000 - ค่าที่ไม่ถูกต้องจะบันทึกไม่ได้" : ((num <= 0m) ? "การ์ดทุกใบถูกตัดออก - เปิดใช้งานอย่างน้อยหนึ่งใบ" : $"{cards.Count((SummonCardOption c) => !c.IsStory)} ใบสุ่มได้ · {cards.Count((SummonCardOption c) => c.IsStory)} ใบกำหนดตายตัวจากเนื้อเรื่อง · {cards.Count((SummonCardOption c) => c.Weight > 0)} ใบเปิดใช้งาน  |  เซอร์แวนต์ {num2 * 100m / num:0.####}% · Craft Essence {(num - num2) * 100m / num:0.####}%  |  รวม 100%"));
		if (dirty)
		{
			StatusText.Text = "มีการเปลี่ยนแปลงที่ยังไม่ได้บันทึก เมื่อบันทึกแล้ว เซิร์ฟเวอร์จะใช้อัตราใหม่ในการสุ่มครั้งถัดไป หน้านี้ใช้ตั้งอัตราการสุ่มเท่านั้น ไม่ได้สุ่มหรือมอบการ์ดใด ๆ";
		}
	}

	private void SetWeights(Func<SummonCardOption, int> selector)
	{
		CardsGrid.CancelEdit();
		CardsGrid.CancelEdit(DataGridEditingUnit.Row);
		batching = true;
		foreach (SummonCardOption card in cards)
		{
			card.WeightText = ((!card.IsStory) ? selector(card) : 0).ToString(CultureInfo.InvariantCulture);
		}
		batching = false;
		dirty = true;
		Recalculate();
	}

	private void EqualAll_OnClick(object sender, RoutedEventArgs e)
	{
		SetWeights((SummonCardOption _) => 1);
	}

	private void OnlyOne_OnClick(object sender, RoutedEventArgs e)
	{
		if (CardsGrid.SelectedItems.Count != 1)
		{
			StatusText.Text = "เลือกการ์ดหนึ่งใบก่อน แล้วจึงตั้งเป็น 100%";
			return;
		}
		SummonCardOption selected = (SummonCardOption)CardsGrid.SelectedItem;
		if (selected.IsStory)
		{
			StatusText.Text = "การ์ดที่กำหนดตายตัวจากเนื้อเรื่องตั้งให้สุ่มไม่ได้";
			return;
		}
		SetWeights((SummonCardOption c) => (c == selected) ? 1 : 0);
	}

	private void EqualSelected_OnClick(object sender, RoutedEventArgs e)
	{
		HashSet<SummonCardOption> selected = (from SummonCardOption c in CardsGrid.SelectedItems
			where !c.IsStory
			select c).ToHashSet();
		if (selected.Count == 0)
		{
			StatusText.Text = "เลือกการ์ดที่ต้องการให้แบ่งโอกาสเท่ากันก่อน";
			return;
		}
		SetWeights((SummonCardOption c) => selected.Contains(c) ? 1 : 0);
	}

	private void Exclude_OnClick(object sender, RoutedEventArgs e)
	{
		HashSet<SummonCardOption> selected = CardsGrid.SelectedItems.Cast<SummonCardOption>().ToHashSet();
		if (selected.Count == 0)
		{
			StatusText.Text = "เลือกการ์ดที่ต้องการตัดออกก่อน";
			return;
		}
		if (cards.Any((SummonCardOption c) => !c.Valid))
		{
			StatusText.Text = "แก้ไขค่าน้ำหนักที่ไม่ถูกต้องก่อน";
			return;
		}
		SetWeights((SummonCardOption c) => (!selected.Contains(c)) ? c.Weight : 0);
	}

	/// <summary>The shape the server reads, keyed by card id, so a preset carries to any install.</summary>
	private string BuildWeightsJson()
	{
		JsonObject jsonObject = new JsonObject();
		foreach (SummonCardOption item in cards.Where((SummonCardOption c) => !c.IsStory))
		{
			jsonObject[item.TcId.ToString(CultureInfo.InvariantCulture)] = item.Weight;
		}
		return new JsonObject
		{
			["version"] = 1,
			["weights"] = jsonObject
		}.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		});
	}

	private void Save_OnClick(object sender, RoutedEventArgs e)
	{
		if (!CardsGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true) || !CardsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true))
		{
			return;
		}
		Recalculate();
		if (!SaveButton.IsEnabled)
		{
			return;
		}
		try
		{
			if ((File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null) != loadedText)
			{
				throw new IOException("หน้าต่างอื่นเปลี่ยนไฟล์อัตราไปแล้ว - คลิกโหลดใหม่ก่อนแก้ไข");
			}
			string contents = BuildWeightsJson();
			AtomicFile.WriteAllText(settingsPath, contents);
			loadedText = contents;
			dirty = false;
			StatusText.Text = "บันทึกลงค่าตั้งของเซิร์ฟเวอร์แล้ว เซิร์ฟเวอร์ที่รองรับจะอ่านอัตราใหม่ในการสุ่มครั้งถัดไปโดยไม่ต้องเริ่มเกมใหม่ หากเพิ่งเปลี่ยนโค้ดเซิร์ฟเวอร์รุ่นเก่า ให้เริ่มเซิร์ฟเวอร์ใหม่หนึ่งครั้ง";
		}
		catch (Exception ex)
		{
			StatusText.Text = "บันทึกไม่สำเร็จ - ไฟล์เดิมไม่ถูกเปลี่ยนแปลง: " + ex.Message;
		}
	}

	public bool DiscardConfirmed()
	{
		if (dirty)
		{
			return ThemedMessageBox.Show("ละทิ้งการเปลี่ยนแปลงอัตราการสุ่มที่ยังไม่ได้บันทึกหรือไม่?", "อัตราการสุ่ม", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
		}
		return true;
	}

	private void Reload_OnClick(object sender, RoutedEventArgs e)
	{
		if (DiscardConfirmed())
		{
			LoadSettings();
		}
	}

	private string PresetsFolder => Path.Combine(Path.GetDirectoryName(settingsPath), "summon-presets");

	/// <summary>This page is a UserControl, so the dialogs it opens belong to the window around it.</summary>
	private Window OwnerWindow => Window.GetWindow(this);

	private void RefreshPresetList(string? select = null)
	{
		PresetFolder.Fill(PresetComboBox, PresetsFolder, select);
	}

	private static bool LooksLikePreset(string text)
	{
		try
		{
			return JsonNode.Parse(text) is JsonObject o && o["version"]?.GetValue<int>() == 1 && o["weights"] is JsonObject;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private void PresetSave_OnClick(object sender, RoutedEventArgs e)
	{
		if (!CardsGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true) || !CardsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true))
		{
			return;
		}
		Recalculate();
		if (!SaveButton.IsEnabled)
		{
			return;
		}
		NamePromptDialog prompt = new NamePromptDialog(OwnerWindow, "บันทึกพรีเซ็ต", "ชื่อของตารางอัตรานี้", PresetComboBox.SelectedItem as string ?? "");
		if (prompt.ShowDialog() != true || prompt.Result == null)
		{
			return;
		}
		string target = PresetFolder.PathFor(PresetFolder.Ensure(PresetsFolder), prompt.Result);
		if (File.Exists(target) && ThemedMessageBox.Show(OwnerWindow, "แทนที่พรีเซ็ต \"" + prompt.Result + "\" หรือไม่?", "บันทึกพรีเซ็ต", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		try
		{
			AtomicFile.WriteAllText(target, BuildWeightsJson());
			RefreshPresetList(prompt.Result);
			StatusText.Text = "บันทึกพรีเซ็ตแล้ว: " + prompt.Result + " อัตราที่ใช้งานจริงจะยังไม่เปลี่ยนจนกว่าจะคลิกบันทึก ส่วนส่งออกจะสร้างสำเนาไว้แบ่งปัน";
		}
		catch (Exception ex)
		{
			StatusText.Text = "บันทึกพรีเซ็ตไม่สำเร็จ: " + ex.Message;
		}
	}

	private void ApplyPresetFile(string path, string name)
	{
		int skipped = ApplyWeightsText(File.ReadAllText(path), tolerant: true);
		dirty = true;
		StatusText.Text = "โหลดพรีเซ็ตแล้ว: " + name + ((skipped == 1) ? " (มีการ์ด 1 ใบในไฟล์ที่ไม่มีอยู่ในเกมของคุณ จึงถูกข้ามไป)" : ((skipped > 1) ? $" (มีการ์ด {skipped} ใบในไฟล์ที่ไม่มีอยู่ในเกมของคุณ จึงถูกข้ามไป)" : "")) + " คลิกบันทึกเพื่อให้เซิร์ฟเวอร์ใช้งาน";
	}

	private void PresetLoad_OnClick(object sender, RoutedEventArgs e)
	{
		if (!(PresetComboBox.SelectedItem is string name) || (dirty && !DiscardConfirmed()))
		{
			return;
		}
		try
		{
			ApplyPresetFile(PresetFolder.PathFor(PresetsFolder, name), name);
		}
		catch (Exception ex)
		{
			StatusText.Text = "โหลดพรีเซ็ตไม่สำเร็จ: " + ex.Message;
		}
	}

	private void PresetDelete_OnClick(object sender, RoutedEventArgs e)
	{
		if (!(PresetComboBox.SelectedItem is string name) || ThemedMessageBox.Show(OwnerWindow, "ลบพรีเซ็ต \"" + name + "\" หรือไม่?", "ลบพรีเซ็ต", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		File.Delete(PresetFolder.PathFor(PresetsFolder, name));
		RefreshPresetList();
		StatusText.Text = "ลบพรีเซ็ตแล้ว: " + name;
	}

	private void PresetExport_OnClick(object sender, RoutedEventArgs e)
	{
		if (!(PresetComboBox.SelectedItem is string name))
		{
			StatusText.Text = "เลือกพรีเซ็ตที่จะส่งออก หรือบันทึกเป็นก่อน";
			return;
		}
		try
		{
			if (PresetFolder.Export(OwnerWindow, PresetFolder.PathFor(PresetsFolder, name), name))
			{
				StatusText.Text = "ส่งออกแล้ว: " + name + ".json - ส่งให้ใครก็ได้ที่มีตัวเรียกเกม แล้วให้เขาเพิ่มด้วยปุ่มนำเข้า";
			}
		}
		catch (Exception ex)
		{
			StatusText.Text = "ส่งออกพรีเซ็ตไม่สำเร็จ: " + ex.Message;
		}
	}

	private void PresetImport_OnClick(object sender, RoutedEventArgs e)
	{
		if (dirty && !DiscardConfirmed())
		{
			return;
		}
		try
		{
			string? name = PresetFolder.Import(OwnerWindow, PresetsFolder, LooksLikePreset, "ไฟล์นั้นไม่ใช่พรีเซ็ตอัตราการสุ่ม ให้เลือกไฟล์ .json ที่ส่งออกจากแถวพรีเซ็ต");
			if (name == null)
			{
				return;
			}
			RefreshPresetList(name);
			ApplyPresetFile(PresetFolder.PathFor(PresetsFolder, name), name);
		}
		catch (Exception ex)
		{
			StatusText.Text = "นำเข้าพรีเซ็ตไม่สำเร็จ: " + ex.Message;
		}
	}

	private void PresetFolderLink_OnClick(object sender, RoutedEventArgs e)
	{
		PresetFolder.OpenFolder(PresetsFolder);
	}
}
