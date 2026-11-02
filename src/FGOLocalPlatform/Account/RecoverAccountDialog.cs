using System.Windows;
using System.Windows.Markup;

namespace FGOLocalPlatform;

public partial class RecoverAccountDialog : Window, IComponentConnector
{
	public string RecoveryKey => KeyTextBox.Text.Trim();

	public RecoverAccountDialog(Window owner, string host)
	{
		InitializeComponent();
		WindowTheme.Apply(this);
		base.Owner = owner;
		LabelText.Text = "พิมพ์หรือวางคีย์กู้คืนของบัญชีคุณบน " + host + ".";
		KeyTextBox.Focus();
	}

	private void RecoverButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (CommunityServer.RecoveryKeyProblem(RecoveryKey) is string problem)
		{
			KeyProblemText.Text = problem;
			KeyProblemText.Visibility = Visibility.Visible;
			KeyTextBox.Focus();
			KeyTextBox.SelectAll();
			return;
		}
		base.DialogResult = true;
	}
}
