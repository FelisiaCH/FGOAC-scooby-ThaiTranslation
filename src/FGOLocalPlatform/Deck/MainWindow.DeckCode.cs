using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private void DeckCodeCopyButton_OnClick(object sender, RoutedEventArgs e)
	{
		int count = cardCollection.SelectedCards.Count;
		if (count == 0)
		{
			HoldStatus("เด็คว่างเปล่า - ไม่มีอะไรให้คัดลอก");
			return;
		}
		string code = DeckCode.Encode(cardCollection.SelectedCards.Select(DeckCode.SlotOf).ToList(), LoadoutComboBox.SelectedItem as string ?? "");
		try
		{
			Clipboard.SetText(code);
		}
		catch (ExternalException)
		{
			HoldStatus("คลิปบอร์ดไม่ว่าง กดคัดลอกรหัสอีกครั้ง", problem: true);
			return;
		}
		HoldStatus($"คัดลอกรหัสเด็คแล้ว: {Cards(count)}");
	}

	private void DeckCodeImportButton_OnClick(object sender, RoutedEventArgs e)
	{
		ImportDeckCode();
	}

	private void DeckCodeTextBox_OnKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			e.Handled = true;
			ImportDeckCode();
		}
	}

	private void DeckCodeTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
	{
		DeckCodeHint.Visibility = (DeckCodeTextBox.Text.Length == 0) ? Visibility.Visible : Visibility.Collapsed;
	}

	/// <summary>
	/// Fills the deck from the pasted code with the cards this PC has images for, in the code's order, and
	/// saves it as a loadout under the code's name, never over a loadout already there.
	/// </summary>
	private void ImportDeckCode()
	{
		if (DeckCodeTextBox.Text.Trim().Length == 0)
		{
			HoldStatus("วางรหัสเด็คก่อน");
			return;
		}
		DeckCode.Reading reading = DeckCode.Decode(DeckCodeTextBox.Text);
		if (reading.Refusal != null)
		{
			HoldStatus(reading.Refusal, problem: true);
			return;
		}
		// Each card image is one card id and kind; the lowest file name wins should two ever share them.
		Dictionary<(int, bool), string> images = new Dictionary<(int, bool), string>();
		foreach (Card card in cardCollection.Cards.Concat(cardCollection.SelectedCards).Where((Card c) => c.CopyNumber == 1).OrderBy((Card c) => c.FileName, StringComparer.OrdinalIgnoreCase))
		{
			images.TryAdd((card.TrcId, card.CardTypeId == 2), card.Path);
		}
		List<string> paths = new List<string>();
		List<int> copies = new List<int>();
		foreach (DeckCode.Slot slot in reading.Slots)
		{
			if (images.TryGetValue((slot.CardId, slot.CraftEssence), out string? path))
			{
				paths.Add(path);
				copies.Add(slot.Copy);
			}
		}
		int total = reading.Slots.Count;
		int missing = total - paths.Count;
		if (paths.Count == 0)
		{
			HoldStatus($"เด็คนั้นมี {Cards(total)} แต่ไม่มีอยู่ในเครื่องนี้เลย", problem: true);
			return;
		}
		cardCollection.Reload(paths, null, copies);
		ApplyOwnedCardFilter();
		PublishDeck();
		DeckCodeTextBox.Clear();
		int held = cardCollection.SelectedCards.Count;
		string status = $"โหลดเด็คจากรหัสแล้ว: ได้ {held} จาก {Cards(total)}" + ((missing > 0) ? $", {missing} ใบไม่อยู่ในเครื่องนี้" : "") + ((total - missing > held) ? $", เกินกว่าที่บัญชีมี {total - missing - held} ใบ" : "");
		string name = reading.Name.Trim();
		try
		{
			name = PresetFolder.FreeName(PresetFolder.Ensure(LoadoutFolder), (name.Length == 0) ? $"Imported {DateTime.Now:yyyy-MM-dd}" : name);
			AtomicFile.WriteAllText(PresetFolder.PathFor(LoadoutFolder, name), LoadoutJson(name));
			RefreshLoadoutList(name);
			HoldStatus(status + " บันทึกเป็นชุดเด็ค " + name + ".");
		}
		catch (Exception ex)
		{
			HoldStatus(status + " บันทึกชุดเด็คไม่ได้: " + ex.Message, problem: true);
		}
	}

	private static string Cards(int count)
	{
		return (count == 1) ? "การ์ด 1 ใบ" : $"การ์ด {count} ใบ";
	}
}
