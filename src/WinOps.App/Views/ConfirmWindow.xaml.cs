using System.Windows;
using WinOps.Core.Models;

namespace WinOps.App.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow(ActionDefinition action, string details)
    {
        InitializeComponent();
        RiskLabel.Text = action.RiskLevel.ToString().ToUpperInvariant().Replace("LEVEL", "LEVEL ");
        TitleText.Text = action.Title;
        DescriptionText.Text = action.Description;
        var requirements = new List<string>
        {
            action.RequiresAdministrator ? "Requires administrator privileges" : "Standard user permissions",
            action.HasRollback ? "Rollback information is available" : "No automatic rollback",
            "Audited: " + action.LoggingRequirement,
            "Validation: " + action.ValidationRequirement
        };
        RequirementsText.Text = string.Join("  •  ", requirements);
        DetailsText.Text = string.IsNullOrWhiteSpace(details) ? string.Empty : "Details: " + details;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
