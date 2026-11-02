using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

public partial class CardVariantsWindow : Window, IComponentConnector
{
	public sealed class Choice
	{
		public Card Card { get; init; }

		public string Label => CardFormState.FormLabel(Card);

		public int Owned { get; init; }

		public int Selected { get; init; }

		public int Maximum { get; init; }

		public string Quantity { get; set; } = "0";
	}

	private readonly int slots;

	public List<Choice> Choices { get; }

	public Dictionary<ushort, int> Quantities { get; private set; } = new Dictionary<ushort, int>();

	public CardVariantsWindow(Card card, IEnumerable<Card> available, IEnumerable<Card> selected, IReadOnlyDictionary<int, int> owned, bool ownedOnly)
	{
		CardVariantsWindow cardVariantsWindow = this;
		InitializeComponent();
		WindowTheme.Apply(this);
		List<Card> library = available.ToList();
		List<Card> list = selected.ToList();
		slots = 30 - list.Count;
		base.DataContext = CardStack.BuildEntities(new Card[1] { card }).Single();
		Choices = (from f in CardFormState.Build(card, library, list, owned)
			select new Choice
			{
				Card = f.Card,
				Owned = f.Owned,
				Selected = f.Selected,
				Maximum = Math.Max(0, Math.Min(cardVariantsWindow.slots, Math.Min(library.Count((Card c) => c.TrcId == f.Card.TrcId), ownedOnly ? f.Available : 30)))
			}).ToList();
		Forms.ItemsSource = Choices;
		if (card.CardTypeId == 2)
		{
			base.Height = 820.0;
			base.MinHeight = 650.0;
			CraftPreview.Visibility = Visibility.Visible;
			CraftImage.Source = card.Bitmap;
			CraftEffects.Effect effect = CraftEffects.Get(CardFormState.EntityKey(card));
			NormalEffect.Text = effect?.Normal ?? "ไม่พบข้อความเอฟเฟกต์แบบปกติ";
			MaximumEffect.Text = effect?.Maximum ?? "ไม่พบข้อความเอฟเฟกต์ปลดขีดจำกัดสูงสุด";
			NormalEffect.ToolTip = effect?.NormalJapanese;
			MaximumEffect.ToolTip = effect?.MaximumJapanese;
		}
		Hint.Text = $"เหลือช่องว่างในเด็ค {slots} ช่อง ดับเบิลคลิกที่แถวเพื่อเลือกจำนวนแล้วเพิ่ม หรือกรอกหลายแถวแล้วเพิ่มพร้อมกันทีเดียว" + (ownedOnly ? " เพิ่มได้เฉพาะการ์ดที่บัญชีนี้มีอยู่เท่านั้น" : " กำลังดูคลังการ์ดทั้งหมด");
	}

	private void Forms_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		DataGrid forms = Forms;
		object originalSource = e.OriginalSource;
		if (!(ItemsControl.ContainerFromElement(forms, (DependencyObject)((originalSource is DependencyObject) ? originalSource : null)) is DataGridRow { Item: Choice item }))
		{
			return;
		}
		e.Handled = true;
		if (item.Maximum < 1)
		{
			Error.Text = "เพิ่มการ์ดแบบนี้ไม่ได้ หรือเด็คเต็มแล้ว";
			return;
		}
		CardQuantityWindow cardQuantityWindow = new CardQuantityWindow(item.Label, item.Maximum, add: true)
		{
			Owner = this
		};
		if (cardQuantityWindow.ShowDialog() == true)
		{
			Quantities = new Dictionary<ushort, int> { [item.Card.TrcId] = cardQuantityWindow.Quantity };
			base.DialogResult = true;
		}
	}

	private void Forms_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (CraftPreview.Visibility == Visibility.Visible && Forms.SelectedItem is Choice choice)
		{
			CraftImage.Source = choice.Card.Bitmap;
		}
	}

	private void Add_OnClick(object sender, RoutedEventArgs e)
	{
		Forms.CommitEdit();
		Forms.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
		Dictionary<ushort, int> dictionary = new Dictionary<ushort, int>();
		foreach (Choice choice in Choices)
		{
			if (!int.TryParse(choice.Quantity, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result < 0 || result > choice.Maximum)
			{
				Error.Text = $"{choice.Label}: กรอกจำนวนเต็มตั้งแต่ 0 ถึง {choice.Maximum}";
				return;
			}
			if (result > 0)
			{
				dictionary[choice.Card.TrcId] = result;
			}
		}
		if (dictionary.Values.Sum() < 1 || dictionary.Values.Sum() > slots)
		{
			Error.Text = $"เลือกการ์ด 1 ถึง {slots} ใบ";
		}
		else
		{
			Quantities = dictionary;
			base.DialogResult = true;
		}
	}
}
