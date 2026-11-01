using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private string LoadoutFolder => Path.Combine(GamePaths.GameRoot, "deck-loadouts");

	private void RefreshLoadoutList(string? select = null)
	{
		PresetFolder.Fill(LoadoutComboBox, LoadoutFolder, select);
		LoadoutChips.ItemsSource = LoadoutComboBox.ItemsSource;
	}

	/// <summary>
	/// A loadout holds card file names, never paths: every install's card folder holds the same
	/// names, so the file a player shares loads on someone else's machine.
	/// </summary>
	private string LoadoutJson(string name)
	{
		JsonArray cards = new JsonArray();
		foreach (Card card in cardCollection.SelectedCards)
		{
			cards.Add(new JsonObject
			{
				["file"] = card.FileName,
				["copy"] = card.CopyNumber
			});
		}
		return new JsonObject
		{
			["format"] = "fgoac-loadout",
			["version"] = 1,
			["name"] = name,
			["cards"] = cards
		}.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		}) + Environment.NewLine;
	}

	private static bool LooksLikeLoadout(string text)
	{
		try
		{
			return JsonNode.Parse(text) is JsonObject o && o["format"]?.GetValue<string>() == "fgoac-loadout" && o["cards"] is JsonArray;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private void LoadoutSaveButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (cardCollection.SelectedCards.Count == 0)
		{
			HoldStatus("เด็คว่างเปล่า - ไม่มีอะไรให้บันทึก");
			return;
		}
		NamePromptDialog prompt = new NamePromptDialog(this, "บันทึกชุดเด็ค", "ชื่อสำหรับเด็คนี้", LoadoutComboBox.SelectedItem as string ?? "");
		if (DialogSink.ShowDialog(prompt) != true || prompt.Result == null)
		{
			return;
		}
		string target = PresetFolder.PathFor(PresetFolder.Ensure(LoadoutFolder), prompt.Result);
		if (File.Exists(target) && ThemedMessageBox.Show(this, "แทนที่ชุดเด็ค \"" + prompt.Result + "\" หรือไม่", "บันทึกชุดเด็ค", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		try
		{
			AtomicFile.WriteAllText(target, LoadoutJson(prompt.Result));
			RefreshLoadoutList(prompt.Result);
			HoldStatus("บันทึกชุดเด็คแล้ว: " + prompt.Result + " ใช้ปุ่มส่งออกเพื่อสร้างสำเนาไว้แบ่งปัน");
		}
		catch (Exception ex)
		{
			HoldStatus("บันทึกชุดเด็คไม่ได้: " + ex.Message);
		}
	}

	private void ApplyLoadoutFile(string path)
	{
		if (!(JsonNode.Parse(File.ReadAllText(path)) is JsonObject loadout) || !(loadout["cards"] is JsonArray cards))
		{
			HoldStatus("ไฟล์นั้นไม่ใช่ชุดเด็ค");
			return;
		}
		List<string> paths = new List<string>();
		List<int> copies = new List<int>();
		int missing = 0;
		foreach (JsonNode? entry in cards)
		{
			// GetFileName drops any folder part, so a shared file cannot point outside the card folder.
			string file = Path.GetFileName(entry?["file"]?.GetValue<string>() ?? "");
			if (file.Length == 0)
			{
				continue;
			}
			// Counted here rather than from what Reload keeps: Reload also stops at 30 cards and
			// drops duplicates, and neither of those is a card the player is missing.
			string cardPath = Path.Combine(cardCollection.Path, file);
			if (!File.Exists(cardPath))
			{
				missing++;
				continue;
			}
			paths.Add(cardPath);
			copies.Add(Math.Clamp(entry?["copy"]?.GetValue<int>() ?? 1, 1, 30));
		}
		if (paths.Count == 0)
		{
			HoldStatus((missing > 0) ? "ไม่มีการ์ดของชุดเด็คนั้นอยู่ในโฟลเดอร์การ์ดของคุณเลย" : "ชุดเด็คนั้นไม่มีการ์ดอยู่เลย");
			return;
		}
		cardCollection.Reload(paths, null, copies);
		ApplyOwnedCardFilter();
		PublishDeck();
		string name = loadout["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(path);
		HoldStatus($"โหลดชุดเด็คแล้ว: {name} - {cardCollection.SelectedCards.Count} การ์ด" + ((missing == 1) ? "; มี 1 ใบที่ไม่อยู่ในโฟลเดอร์การ์ดของคุณ จึงถูกข้ามไป" : ((missing > 1) ? $"; มี {missing} ใบที่ไม่อยู่ในโฟลเดอร์การ์ดของคุณ จึงถูกข้ามไป" : " แล้ว")));
	}

	private void LoadoutLoadButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (LoadoutComboBox.SelectedItem is string name)
		{
			LoadLoadout(name);
		}
	}

	/// <summary>A loadout's button above the deck: loads it and selects it in the list for Delete and Export.</summary>
	private void LoadoutChip_OnClick(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.Tag is string name)
		{
			LoadoutComboBox.SelectedItem = name;
			LoadLoadout(name);
		}
	}

	private void LoadLoadout(string name)
	{
		try
		{
			ApplyLoadoutFile(PresetFolder.PathFor(LoadoutFolder, name));
		}
		catch (Exception ex)
		{
			HoldStatus("โหลดชุดเด็คไม่ได้: " + ex.Message);
		}
	}

	private void LoadoutDeleteButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (!(LoadoutComboBox.SelectedItem is string name) || ThemedMessageBox.Show(this, "ลบชุดเด็ค \"" + name + "\" หรือไม่", "ลบชุดเด็ค", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		File.Delete(PresetFolder.PathFor(LoadoutFolder, name));
		RefreshLoadoutList();
		HoldStatus("ลบชุดเด็คแล้ว: " + name);
	}

	private void LoadoutExportButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (!(LoadoutComboBox.SelectedItem is string name))
		{
			HoldStatus("เลือกชุดเด็คที่จะส่งออก หรือใช้บันทึกเป็นก่อน");
			return;
		}
		try
		{
			if (PresetFolder.Export(this, PresetFolder.PathFor(LoadoutFolder, name), name))
			{
				HoldStatus("ส่งออกแล้ว: " + name + ".json - ส่งไฟล์นี้ให้ใครก็ได้ที่มีตัวเรียกเกม แล้วเขาเพิ่มเข้าไปด้วยปุ่มนำเข้า");
			}
		}
		catch (Exception ex)
		{
			HoldStatus("ส่งออกชุดเด็คไม่ได้: " + ex.Message);
		}
	}

	private void LoadoutImportButton_OnClick(object sender, RoutedEventArgs e)
	{
		try
		{
			string? name = PresetFolder.Import(this, LoadoutFolder, LooksLikeLoadout, "ไฟล์นั้นไม่ใช่ชุดเด็ค เลือกไฟล์ .json ที่ส่งออกมาจากแถวชุดเด็ค");
			if (name == null)
			{
				return;
			}
			RefreshLoadoutList(name);
			ApplyLoadoutFile(PresetFolder.PathFor(LoadoutFolder, name));
		}
		catch (Exception ex)
		{
			HoldStatus("นำเข้าชุดเด็คไม่ได้: " + ex.Message);
		}
	}

	private void LoadoutFolderLink_OnClick(object sender, RoutedEventArgs e)
	{
		PresetFolder.OpenFolder(LoadoutFolder);
	}
}
