using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Applies the responsive project-page layout without coupling it to project data or event handlers.
/// </summary>
public sealed class ProjectLayoutController
{
    public const double NarrowProjectBreakpoint = 820;

    private readonly FrameworkElement _root;
    private readonly Grid _projectsPage;
    private readonly FrameworkElement _listPane;
    private readonly FrameworkElement _detailPane;
    private readonly FrameworkElement _backButton;
    private readonly Grid _metrics;
    private readonly FrameworkElement[] _metricCards;

    public ProjectLayoutController(
        FrameworkElement root,
        Grid projectsPage,
        FrameworkElement listPane,
        FrameworkElement detailPane,
        FrameworkElement backButton,
        Grid metrics,
        params FrameworkElement[] metricCards)
    {
        _root = root;
        _projectsPage = projectsPage;
        _listPane = listPane;
        _detailPane = detailPane;
        _backButton = backButton;
        _metrics = metrics;
        _metricCards = metricCards;
    }

    public bool IsNarrow => _root.ActualWidth > 0 && _root.ActualWidth < NarrowProjectBreakpoint;

    public void Update(bool hasSelectedProject, bool showingProjectList)
    {
        if (_projectsPage.ColumnDefinitions.Count < 2) return;
        var showProjectDetail = hasSelectedProject && !showingProjectList;
        var listColumn = _projectsPage.ColumnDefinitions[0];
        var detailColumn = _projectsPage.ColumnDefinitions[1];

        if (!IsNarrow)
        {
            listColumn.Width = new GridLength(280);
            detailColumn.Width = new GridLength(1, GridUnitType.Star);
            _listPane.Visibility = Visibility.Visible;
            _detailPane.Visibility = Visibility.Visible;
            _backButton.Visibility = Visibility.Collapsed;
            _projectsPage.ColumnSpacing = 16;
        }
        else
        {
            listColumn.Width = showProjectDetail ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            detailColumn.Width = showProjectDetail ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            _listPane.Visibility = showProjectDetail ? Visibility.Collapsed : Visibility.Visible;
            _detailPane.Visibility = showProjectDetail ? Visibility.Visible : Visibility.Collapsed;
            _backButton.Visibility = showProjectDetail ? Visibility.Visible : Visibility.Collapsed;
            _projectsPage.ColumnSpacing = 0;
        }

        // Use the actual data pane, not the window width: navigation occupies space too.
        var detailWidth = IsNarrow ? _root.ActualWidth - 80 : _root.ActualWidth - 400;
        UpdateMetricLayout(detailWidth);
    }

    private void UpdateMetricLayout(double width)
    {
        if (_metrics.ColumnDefinitions.Count < 4 || _metricCards.Length < 4) return;
        var columns = width < 360 ? 1 : width < 800 ? 2 : 4;
        _metrics.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        _metrics.ColumnDefinitions[1].Width = columns >= 2 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        _metrics.ColumnDefinitions[2].Width = columns >= 4 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        _metrics.ColumnDefinitions[3].Width = columns >= 4 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        Place(_metricCards[0], 0, 0);
        Place(_metricCards[1], columns == 1 ? 1 : 0, columns == 1 ? 0 : 1);
        Place(_metricCards[2], columns == 4 ? 0 : columns == 2 ? 1 : 2, columns == 4 ? 2 : 0);
        Place(_metricCards[3], columns == 4 ? 0 : columns == 2 ? 1 : 3, columns == 4 ? 3 : columns == 2 ? 1 : 0);
    }

    private static void Place(FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
    }
}
