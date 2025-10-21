#region using
using NinjaTrader.Cbi;
using NinjaTrader.Custom.AddOns;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using System.Security.Policy;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;
#endregion

namespace NinjaTrader.AddOns
{
	public class AccountDashboardWindow : NTWindow //, IWorkspacePersistence
	{
		public AccountDashboardWindow()
		{
			// set Caption property (not Title), since Title is managed internally to properly combine selected Tab Header and Caption for display in the windows taskbar
			// This is the name displayed in the top-left of the window
			Caption = "RAGE Account Dashboard";

			// Set the initial dimensions of the window
			Width = 1180;
			Height = 720;
		}

		/*public void Save( XDocument doc, XElement elem )
		{
			elem.SetAttributeValue( "Left", Left );
			elem.SetAttributeValue( "Top", Top );
			elem.SetAttributeValue( "Width", Width );
			elem.SetAttributeValue( "Height", Height );
			elem.SetAttributeValue( "IsOpen", IsVisible );
		}

		public void Restore( XDocument doc, XElement elem )
		{
			if(double.TryParse( elem.Attribute( "Left" )?.Value, out var l )) Left = l;
			if(double.TryParse( elem.Attribute( "Top" )?.Value, out var t )) Top = t;
			if(double.TryParse( elem.Attribute( "Width" )?.Value, out var w )) Width = w;
			if(double.TryParse( elem.Attribute( "Height" )?.Value, out var h )) Height = h;
			bool isOpen = bool.TryParse( elem.Attribute( "IsOpen" )?.Value, out var open ) && open;
			if(isOpen) Show();
		}

		public WorkspaceOptions WorkspaceOptions 
		{ 
			get; // => throw new NotImplementedException(); 
			set; // => throw new NotImplementedException(); 
		}*/
	}

	/*class SimpleInputDialog : Window
	{
		public double Value { get; private set; }

		public SimpleInputDialog( string title, string prompt, double defaultValue = 1 )
		{
			Title = title;
			Width = 250;
			Height = 140;
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			Background = Brushes.DimGray;
			Foreground = Brushes.White;
			ResizeMode = ResizeMode.NoResize;

			var stack = new StackPanel { Margin = new Thickness( 10 ) };
			stack.Children.Add( new TextBlock { Text = prompt, Margin = new Thickness( 0, 0, 0, 6 ) } );

			var input = new TextBox { Text = defaultValue.ToString(), Margin = new Thickness( 0, 0, 0, 10 ) };
			stack.Children.Add( input );

			var ok = new Button { Content = "OK", Width = 60, HorizontalAlignment = HorizontalAlignment.Right };
			ok.Click += ( s, e ) =>
			{
				if(double.TryParse( input.Text, out double v ))
					Value = v;
				DialogResult = true;
			};
			stack.Children.Add( ok );

			Content = stack;
		}
	}*/


	public partial class AccountDashboard : AddOnBase, IWorkspacePersistence
	{
        //NTWindow win;
		AccountDashboardWindow dash;

		internal DataGrid MainGrid;

		NTMenuItem ccNewMenu;
		NTMenuItem myMenuItem;

		bool showSim = true; // track toggle state
		bool copierEnabled = false;

		public WorkspaceOptions WorkspaceOptions
		{
			get => default;   // no ctor call
			set { /* ignore */ }
		}
		//public WorkspaceOptions WorkspaceOptions { get; set; } = new WorkspaceOptions( "RAGEAccountDashboard", null );

		public void Save( XDocument doc, XElement elem )
		{
			bool open = dash != null && dash.IsVisible;
			elem.SetAttributeValue( "IsOpen", open );
			if(!open) return;

			elem.SetAttributeValue( "Left", dash.Left );
			elem.SetAttributeValue( "Top", dash.Top );
			elem.SetAttributeValue( "Width", dash.Width );
			elem.SetAttributeValue( "Height", dash.Height );
		}

		public void Restore( XDocument doc, XElement elem )
		{
			bool shouldOpen = bool.TryParse( elem.Attribute( "IsOpen" )?.Value, out var v ) && v;
			if(!shouldOpen) return;

			dash = new AccountDashboardWindow
			{
				// Owner optional; safe default:
				Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault( w => w is ControlCenter ),
				Content = BuildUI()
			};

			if(double.TryParse( elem.Attribute( "Left" )?.Value, out var l )) dash.Left = l;
			if(double.TryParse( elem.Attribute( "Top" )?.Value, out var t )) dash.Top = t;
			if(double.TryParse( elem.Attribute( "Width" )?.Value, out var w )) dash.Width = w;
			if(double.TryParse( elem.Attribute( "Height" )?.Value, out var h )) dash.Height = h;

			dash.Show();
		}


		protected override void OnWindowCreated(Window w)
        {
            if (w is ControlCenter cc)
            {
				// "New" menu in Control Center
				ccNewMenu = cc.FindFirst( "ControlCenterMenuItemNew" ) as NTMenuItem;
				if(ccNewMenu == null) return;

				if(myMenuItem != null) return; // avoid duplicates

				myMenuItem = new NTMenuItem { Header = "RAGE Account Dashboard" };
				myMenuItem.FontSize = 11;
				myMenuItem.Click += ( s, e ) =>
				{
					dash = new AccountDashboardWindow
					{
						Title = "RAGE Account Dashboard",
						Owner = cc,
						Content = BuildUI()
					};

					dash.Loaded += ( s2, e2 ) =>
					{
						// HACK: Avoid that first connect pulse
						//
						//addedAccounts = Account.All
						//	.Where( a => a.ConnectionStatus == ConnectionStatus.Connected )
						//	.Select( a => a.Name )
						//	.ToHashSet( StringComparer.OrdinalIgnoreCase );
						prevAccounts = Account.All
							.Where( a => a.ConnectionStatus == ConnectionStatus.Connected )
							.Select( a => a.Name )
							.ToHashSet( StringComparer.OrdinalIgnoreCase );

						// Populate and subscribe when the window actually opens
						//Print( "Dash Loaded" );
						InitializeAccounts();
					};

					dash.Closed += ( s3, e3 ) =>
					{
						// Populate and subscribe when the window actually opens
						//Print( "Dash Closed" );
						UninitializeAccounts();
					};
					dash.Show();
				};
				ccNewMenu.Items.Add( myMenuItem );
			}
		}

		protected override void OnWindowDestroyed( Window window )
		{
			var cc = window as ControlCenter;
			if(cc == null) return;

			var menu = cc.FindFirst( "ControlCenterMenuItemNew" ) as NTMenuItem;
			if(menu != null && myMenuItem != null)
				menu.Items.Remove( myMenuItem );

			myMenuItem = null;
		}


		UIElement BuildUI()
        {
			var pnlBrush = new PnLBrushConverter();
            var posBg = new PosBackgroundConverter();
            var roleBg = new RoleRowBackgroundConverter();

            var root = new Grid { Background = ADTheme.Bg };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });           // toolbar
			root.RowDefinitions.Add( new RowDefinition { Height = GridLength.Auto } );           // spacer
//			root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // main grid
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });           // spacer
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });           // summaries

			//root.SetValue( Control.FontFamilyProperty, new FontFamily( "Lato" ) );
			//root.SetValue( Control.FontFamilyProperty, new FontFamily( "Roboto" ) );
			//root.SetValue( Control.FontFamilyProperty, new FontFamily( "Consolas" ) );
			root.SetValue( Control.FontFamilyProperty, new FontFamily( "Segoe UI" ) );
			root.SetValue( Control.FontSizeProperty, 13.0 );
			root.SetValue( TextOptions.TextFormattingModeProperty, TextFormattingMode.Display );

			// Toolbar
			//
			var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6), Height = 28 };

			Border copierBorder = null;
			{
				// --- Copier ON/OFF circle toggle ---
				copierBorder = new Border
				{
					Width = 20,
					Height = 20,
					CornerRadius = new CornerRadius( 10 ),
					Background = Brushes.DimGray,
					Margin = new Thickness( 0, 0, 10, 0 ),
					Cursor = Cursors.Hand,
					ToolTip = "Enable / Disable Copier"
				};

				// handle clicks manually
				copierBorder.MouseLeftButtonUp += ( s, e ) =>
				{
					if(!Rows.Any( r => r.Role == RowRole.Master ))
					{
						copierEnabled = false;
						copierBorder.Background = Brushes.DimGray;
						return;
					}

					copierEnabled = !copierEnabled;
					copierBorder.Background = copierEnabled ? Brushes.LimeGreen : Brushes.IndianRed;
				};
				bar.Children.Add( copierBorder );
			}


			var showAll = new Button { Content = "Show All", Margin = new Thickness(0, 0, 8, 0) };
            showAll.Click += (s, e) => { foreach (var r in Rows) r.Hidden = false; View.Refresh(); };
            var saveBtn = new Button { Content = "Save Settings", Margin = new Thickness(0, 0, 6, 0) };
            saveBtn.Click += (s, e) => SaveSettings();
            var loadBtn = new Button { Content = "Load Settings", Margin = new Thickness(0, 0, 6, 0) };
            loadBtn.Click += (s, e) => { LoadSettings(); View.Refresh(); };
            bar.Children.Add(showAll); bar.Children.Add(saveBtn); bar.Children.Add(loadBtn);


			var simToggle = new CheckBox
			{
				Content = "Show Sim Accounts",
				Margin = new Thickness( 0, 0, 10, 0 ),
				VerticalAlignment = VerticalAlignment.Center,
				Foreground = ADTheme.Fore,
				IsChecked = showSim
			};
			simToggle.Checked += ( s, e ) => { showSim = true; View.Refresh(); };
			simToggle.Unchecked += ( s, e ) => { showSim = false; View.Refresh(); };

			bar.Children.Add( simToggle );

			Grid.SetRow(bar, 0); root.Children.Add(bar);

			// Main grid
			var dg = new DataGrid
			{
				ItemsSource = View,
				AutoGenerateColumns = false,
				HeadersVisibility = DataGridHeadersVisibility.Column,
				GridLinesVisibility = DataGridGridLinesVisibility.None,
				//RowBackground = ADTheme.RowBg,
				//AlternatingRowBackground = ADTheme.AltRowBg,	// Can't have this and Role tinting at same time
				Foreground = ADTheme.Fore,
				CanUserAddRows = false,
				IsReadOnly = false,
				Background = ADTheme.RowBg,
				BorderThickness = new Thickness( 0 )
			};


			dg.LoadingRow += ( s, e ) =>
			{
				if(e.Row.Item is AccountRow ar)
				{
					ar.RowRef = e.Row;

					// if this row was just added, pulse immediately
					if(addedAccounts?.Contains( ar.AccountName ) == true)
						PulseRow( ar, ADTheme.ConnectRow );
				}
			};
			dg.UnloadingRow += ( s, e ) =>
			{
				if(e.Row.Item is AccountRow ar)
				{
					ar.RowRef = null;
				}
			};

			// Custom Header style
			{
				var headerStyle = new Style( typeof( DataGridColumnHeader ), (Style)Application.Current.TryFindResource( typeof( DataGridColumnHeader ) ) );
				headerStyle.Setters.Add( new Setter( Control.BackgroundProperty, new SolidColorBrush( Color.FromRgb( 0x50, 0x45, 0x45 ) ) ) );
				headerStyle.Setters.Add( new Setter( Control.ForegroundProperty, ADTheme.Fore ) );
				headerStyle.Setters.Add( new Setter( Control.FontWeightProperty, FontWeights.SemiBold ) );
				headerStyle.Setters.Add( new Setter( Control.FontSizeProperty, 14.0 ) );
				headerStyle.Setters.Add( new Setter( Control.BorderBrushProperty, ADTheme.RowBg ) );
				headerStyle.Setters.Add( new Setter( Control.BorderThicknessProperty, new Thickness( 1, 0, 0, 0 ) ) );
				//headerStyle.Setters.Add( new Setter( Control.MarginProperty, new Thickness( 0, 4, 8, 4 ) ) );
				headerStyle.Setters.Add( new Setter( Control.HeightProperty, 30.0 ) );
				headerStyle.Setters.Add( new Setter( Control.PaddingProperty, new Thickness( 6, 0, 6, 0 ) ) );
				headerStyle.Setters.Add( new Setter( Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch ) );

				dg.ColumnHeaderStyle = headerStyle;
			}

			// remove focus rectangle and selection border
			/*dg.FocusVisualStyle = null;
			dg.CellStyle = new Style( typeof( DataGridCell ) )
			{
				Setters =
				{
					new Setter(DataGridCell.FocusVisualStyleProperty, null),
					new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)),
					new Setter(DataGridCell.BorderBrushProperty, Brushes.Transparent)
				}
			};*/

			//dg.ColumnWidth = DataGridLength.Auto;
			//dg.CanUserResizeColumns = true;
			//dg.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

			// Deselect any rows by default
			dg.Loaded += ( s, e ) => dg.SelectedIndex = -1;

			// Role indicator column
			/*var roleTpl = new DataTemplate();
			var text = new FrameworkElementFactory( typeof( TextBlock ) );
			text.SetBinding( TextBlock.TextProperty, new Binding( "Role" ) );
			text.SetValue( TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center );
			text.SetValue( TextBlock.FontWeightProperty, FontWeights.Bold );
			text.SetValue( TextBlock.ForegroundProperty, ADTheme.White );

			// convert enum to short label
			text.SetValue( TextBlock.TextProperty, "" );
			text.SetBinding( TextBlock.TextProperty, new Binding( "Role" )
			{
				Converter = new RoleLabelConverter()
			} );

			roleTpl.VisualTree = text;

			// single-click to toggle
			var colRole = new DataGridTemplateColumn
			{
				Header = "Role",
				CellTemplate = roleTpl,
				Width = 40
			};
			dg.Columns.Add( colRole );

			// event handler to toggle roles when clicked
			dg.PreviewMouseLeftButtonUp += ( s, e ) =>
			{
				var fe = e.OriginalSource as FrameworkElement;
				if(fe?.DataContext is AccountRow r && dg.CurrentColumn?.Header?.ToString() == "Role")
					OnRoleButtonClicked( r );
			};*/

			// Row tint
			var rowStyle = new Style(typeof(DataGridRow));
			rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, new Binding("Role") { Converter = roleBg }));

			// --- selection styling ---
			dg.Resources[ SystemColors.HighlightBrushKey ] = new SolidColorBrush( Color.FromArgb( 0x33, 0x66, 0x66, 0x66 ) );  // background for selected row
			dg.Resources[ SystemColors.HighlightTextBrushKey ] = Brushes.White;  // text color when selected
			// Add these to keep same look when unfocused
			dg.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = new SolidColorBrush(Color.FromArgb(0x33, 0x66, 0x66, 0x66));  // same as active
			dg.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Brushes.White;


			// context menu
			rowStyle.Setters.Add(new Setter(Control.ContextMenuProperty, BuildRowContextMenu()));

            dg.RowStyle = rowStyle;

			dg.MinRowHeight = 24;
			dg.RowHeight = 24;

			// "-" role button
			// --- First column: dynamic role button ---
			var roleBtnTpl = new DataTemplate();
			var btn = new FrameworkElementFactory( typeof( Button ) );

			// bind button label to Role (M/F/empty)
			btn.SetBinding( Button.ContentProperty, new Binding( "Role" )
			{
				Converter = new RoleLabelConverter()  // returns "M", "F", or ""
			} );

			// style
			btn.SetValue( Button.HorizontalAlignmentProperty, HorizontalAlignment.Center );
			btn.SetValue( Button.VerticalAlignmentProperty, VerticalAlignment.Center );
			btn.SetValue( Button.FontSizeProperty, 10.0 );
			btn.SetValue( Button.FontWeightProperty, FontWeights.Bold );
			btn.SetValue( Button.ForegroundProperty, Brushes.White );
			btn.SetValue( Button.BackgroundProperty, Brushes.Transparent );
			btn.SetValue( Button.BorderThicknessProperty, new Thickness( 0 ) );
			btn.SetValue( Button.FocusableProperty, false );

			// click handler
			btn.AddHandler( Button.ClickEvent, new RoutedEventHandler( ( s, e ) =>
			{
				if((s as FrameworkElement)?.DataContext is AccountRow r)
					OnRoleButtonClicked( r );
			} ) );

			roleBtnTpl.VisualTree = btn;

			// replace the "-" header column
			dg.Columns.Add( new DataGridTemplateColumn
			{
				Header = "-",
				CellTemplate = roleBtnTpl,
				Width = 40
			} );
			//dg.Columns.Add(ButtonCol("-", "RoleButtonCmd", 40));

			// Account
			dg.Columns.Add(TextReadOnlyCol("Account", "AccountName", 180));

            // Hide toggle
            /*var hideTpl = new DataTemplate();
            var hideBtn = new FrameworkElementFactory(typeof(CheckBox));
            hideBtn.SetValue(CheckBox.ContentProperty, "");
            hideBtn.SetBinding(CheckBox.IsCheckedProperty, new Binding("Hidden") { Mode = BindingMode.TwoWay });
            hideBtn.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler((s, e) => View.Refresh()));
            hideBtn.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler((s, e) => View.Refresh()));
            hideTpl.VisualTree = hideBtn;
            dg.Columns.Add(new DataGridTemplateColumn { Header = "Hide", CellTemplate = hideTpl, Width = 60 });*/

            // Actions placeholder
            //dg.Columns.Add(TextReadOnlyCol("Actions", "_", 90));

            // Size dropdown 1..10
            /***var multTpl = new DataTemplate();
			var cb = new FrameworkElementFactory( typeof( ComboBox ) );
			cb.SetValue( ComboBox.BackgroundProperty, Brushes.Transparent );
			cb.SetValue( ComboBox.BorderThicknessProperty, new Thickness( 0 ) );
			cb.SetValue( ComboBox.ForegroundProperty, Brushes.White );
			cb.SetValue( ComboBox.MarginProperty, new Thickness( 0 ) );
			cb.SetValue( ComboBox.WidthProperty, 80.0 );
			cb.SetValue( ComboBox.HeightProperty, 24.0 );
			//cb.SetValue( ComboBox.PaddingProperty, new Thickness( 6, 0, 6, 0 ) );
			//cb.SetValue( ComboBox.HorizontalAlignmentProperty, HorizontalAlignment.Center );
			//cb.SetValue( ComboBox.VerticalAlignmentProperty, VerticalAlignment.Center );
			cb.SetValue( ComboBox.HorizontalContentAlignmentProperty, HorizontalAlignment.Left );
			cb.SetValue( ComboBox.VerticalContentAlignmentProperty, VerticalAlignment.Center );
			// center items inside dropdown
			var itemStyle = new Style( typeof( ComboBoxItem ) );
			itemStyle.Setters.Add( new Setter( Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left ) );
			itemStyle.Setters.Add( new Setter( Control.VerticalContentAlignmentProperty, VerticalAlignment.Center ) );
			//itemStyle.Setters.Add( new Setter( Control.PaddingProperty, new Thickness( 6, 0, 6, 0 ) ) );
			itemStyle.Setters.Add( new Setter( Control.WidthProperty, 80.0 ) );
			itemStyle.Setters.Add( new Setter( Control.HeightProperty, 24.0 ) );
			cb.SetValue( ComboBox.ItemContainerStyleProperty, itemStyle );
			cb.SetValue( ComboBox.ItemsSourceProperty, Enumerable.Range( 1, 10 ).Select( i => $"{i}" ).ToList() );
			cb.SetBinding( ComboBox.SelectedIndexProperty, new Binding( "Size" ) { Mode = BindingMode.TwoWay, Converter = new MultToIndexConverter() } );

			// disable when Role == Master
			var roleToEnabled2 = new RoleToEnabledConverter2();
			cb.SetBinding( UIElement.IsEnabledProperty, new Binding( "Role" )
			{
				Converter = roleToEnabled2
			} );
			multTpl.VisualTree = cb;
			var multCol = new DataGridTemplateColumn
			{
				Header = "Size",
				CellTemplate = multTpl,
				Width = 80
			};
			dg.Columns.Add( multCol );***/

			var sizeTB = IntEditableCol( "Size", "Size", 40, TextAlignment.Center );
			var cellStyle = new Style( typeof( DataGridCell ) );
			cellStyle.Setters.Add( new Setter( UIElement.IsEnabledProperty, new Binding( "Role" ) { Converter = new RoleToEnabledConverter2() } ) );
			sizeTB.CellStyle = cellStyle; 
			dg.Columns.Add( sizeTB );


			// Pos with background
			var posTpl = new DataTemplate();
			var posText = new FrameworkElementFactory(typeof(TextBlock));
			posText.SetBinding(TextBlock.TextProperty, new Binding("Pos" ) );
			posText.SetValue( TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center );
			posText.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            posText.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 8, 0));
			posText.SetValue( TextBlock.PaddingProperty, new Thickness( 6, 0, 6, 0 ) );
			var posBorder = new FrameworkElementFactory(typeof(Border));
			posBorder.SetBinding( Border.BackgroundProperty, new Binding( "Dir" ) { Converter = posBg } );
			posBorder.SetValue( TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Left );
			posBorder.AppendChild(posText);
			posTpl.VisualTree = posBorder;
			dg.Columns.Add(new DataGridTemplateColumn { Header = "Pos", CellTemplate = posTpl, Width = 40 });

			// Qty
			//
			//dg.Columns.Add( IntReadOnlyCol( "Qty", "Qty", 60));

			// PnL and balances
			//
			dg.Columns.Add(MoneyCol("Unrealized", "Unrealized", true));
            dg.Columns.Add(MoneyCol("Realized", "Realized", true));
			dg.Columns.Add( MoneyCol( "Total P&L", "TotalPnL", true ) );
			dg.Columns.Add(MoneyCol("Commissions", "Commissions", false));
			dg.Columns.Add(MoneyCol("Cash Value", "CashValue", false));
            //dg.Columns.Add(MoneyCol("Net Liquidation", "NetLiq", false));
			dg.Columns.Add( MoneyCol( "Max Drawdown", "TrailingMaxDrawdown", false ) );
			//dg.Columns.Add(MoneyCol("From Funded", "FromFunded", true));
			//dg.Columns.Add(MoneyCol("Auto Liquidate", "AutoLiquidate", false));
            //dg.Columns.Add(MoneyCol("From Closed", "FromClosed", true));
            //dg.Columns.Add(MoneyCol("From Loss", "FromLoss", true));

            // Goals
			//
            dg.Columns.Add(MoneyEditableCol("Daily Loss", "DailyLoss"));
            dg.Columns.Add(MoneyEditableCol("Daily Goal", "DailyGoal"));

            // Risk column with header toggle
			//
            var riskTpl = new DataTemplate();
            var chk = new FrameworkElementFactory(typeof(CheckBox));
            chk.SetValue(CheckBox.ContentProperty, "");
            chk.SetBinding(CheckBox.IsCheckedProperty, new Binding("RiskEnabled") { Mode = BindingMode.TwoWay });
			// font styling
			chk.SetValue( CheckBox.FontSizeProperty, 13.0 );
			chk.SetValue( CheckBox.FontWeightProperty, FontWeights.SemiBold );
			// optional alignment
			chk.SetValue( CheckBox.HorizontalAlignmentProperty, HorizontalAlignment.Left );
			chk.SetValue( CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center );
			chk.SetValue( CheckBox.PaddingProperty, new Thickness( 6, 0, 6, 0 ) );
			riskTpl.VisualTree = chk;			
			var headerToggle = new CheckBox { Content = "Risk", FontSize = 13.0, FontWeight = FontWeights.SemiBold };
			// Weird 1 padding to align header with the rest
			headerToggle.SetValue( CheckBox.PaddingProperty, new Thickness( 1, 0, 0, 0 ) );
			headerToggle.Checked += (s, e) => Rows.ToList().ForEach(r => r.RiskEnabled = true);
            headerToggle.Unchecked += (s, e) => Rows.ToList().ForEach(r => r.RiskEnabled = false);
            dg.Columns.Add(new DataGridTemplateColumn { Header = headerToggle, CellTemplate = riskTpl, Width = 80 });

            Grid.SetRow(dg, 1);
            root.Children.Add(dg);


            // Spacer
			//
            var spacer = new Border { Height = 36, Background = ADTheme.Bg };
            Grid.SetRow(spacer, 2); 
			root.Children.Add(spacer);


            // Summary grid
			//
            var sum = new DataGrid
            {
                ItemsSource = Summaries,
                AutoGenerateColumns = false,
                HeadersVisibility = DataGridHeadersVisibility.None,
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                RowBackground = ADTheme.RowBg,
                //AlternatingRowBackground = ADTheme.AltRowBg,
                Foreground = ADTheme.Fore,
				FontSize = 13,
                CanUserAddRows = false,
                IsReadOnly = true,
                Margin = new Thickness(0, 0, 0, 6),
				BorderThickness = new Thickness( 0 )
			};

			sum.SelectionMode = DataGridSelectionMode.Single;
			sum.SelectionUnit = DataGridSelectionUnit.FullRow;
			sum.IsHitTestVisible = false;          // ignores mouse clicks
			sum.Focusable = false;

			sum.Background = ADTheme.RowBg;        // keeps dark background
			//sum.Background = Brushes.Transparent;
			sum.HorizontalGridLinesBrush = Brushes.Transparent;
			sum.VerticalGridLinesBrush = Brushes.Transparent;

			// --- selection styling ---
			sum.Resources[ SystemColors.HighlightBrushKey ] = ADTheme.Bg; //  new SolidColorBrush( Color.FromArgb( 0x33, 0x66, 0x66, 0x66 ) );  // background for selected row
			sum.Resources[ SystemColors.HighlightTextBrushKey ] = Brushes.White;  // text color when selected
			// Add these to keep same look when unfocused
			sum.Resources[ SystemColors.InactiveSelectionHighlightBrushKey ] = ADTheme.Bg; //  new SolidColorBrush( Color.FromArgb( 0x33, 0x66, 0x66, 0x66 ) );  // same as active
			sum.Resources[ SystemColors.InactiveSelectionHighlightTextBrushKey ] = Brushes.White;
			sum.Resources[ SystemColors.ActiveBorderColorKey ] = Brushes.Transparent;
			sum.Resources[ SystemColors.InactiveBorderColorKey ] = Brushes.Transparent;

			sum.MinRowHeight = 24;
			sum.RowHeight = 24;

			sum.Columns.Add( IntReadOnlyCol( "Accounts", "AccountCount", 40, TextAlignment.Center));
            sum.Columns.Add(TextReadOnlyCol("Connection", "Label", 180 ) );
			sum.Columns.Add(TextReadOnlyCol( "Size", "-", 40 ));

			// Pos with background
			var posTplS = new DataTemplate();
			var posTextS = new FrameworkElementFactory( typeof( TextBlock ) );
			posTextS.SetBinding( TextBlock.TextProperty, new Binding( "TotalPos" ) );
			posTextS.SetValue( TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center );
			posTextS.SetValue( TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center );
			posTextS.SetValue( TextBlock.MarginProperty, new Thickness( 8, 0, 8, 0 ) );
			posTextS.SetValue( TextBlock.PaddingProperty, new Thickness( 6, 0, 6, 0 ) );
			var posBorderS = new FrameworkElementFactory( typeof( Border ) );
			posBorderS.SetBinding( Border.BackgroundProperty, new Binding( "Dir" ) { Converter = new PosBackgroundConverter() } );
			posBorderS.SetValue( TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Left );
			posBorderS.AppendChild( posTextS );
			posTplS.VisualTree = posBorderS;
			sum.Columns.Add( new DataGridTemplateColumn { Header = "TotalPos", CellTemplate = posTplS, Width = 40 } );

			sum.Columns.Add(MoneyCol("Unrealized", "TotalUnrealized", true));
            sum.Columns.Add(MoneyCol("Realized", "TotalRealized", true));
			sum.Columns.Add(MoneyCol("Total PNL", "TotalPnL", true));
            sum.Columns.Add(MoneyCol("Commissions", "TotalComms", false));
            sum.Columns.Add(MoneyCol("Cash Value", "TotalCash", false));
            //sum.Columns.Add(MoneyCol("Net Liq", "TotalNetLiq", false));
			//sum.Columns.Add( MoneyCol( "Max Drawdown", "-", false ) );
			//sum.Columns.Add( MoneyCol( "Auto Liquidate", "-", false ) );
			//sum.Columns.Add( MoneyCol( "Daily Loss", "-", false ) );
			//sum.Columns.Add( MoneyCol( "Daily Gain", "-", false ) );
			//sum.Columns.Add( TextReadOnlyCol( "Risk", "-", 100 ) );
			//sum.Columns.Add( MoneyCol( "Risk", "-", false ) );
			
            Grid.SetRow(sum, 3);
            root.Children.Add(sum);

			// must be the same ObservableCollection<AccountRow> used in InitializeAccounts()
			dg.ItemsSource = Rows;


			// FOR COPIER STATE
			//
			// update color when master role changes
			void UpdateCopierColor()
			{
				var hasMaster = Rows.Any( r => r.Role == RowRole.Master );
				if(!hasMaster)
				{ 
					copierBorder.Background = Brushes.DimGray;
				}
				else
					copierBorder.Background = copierEnabled ? Brushes.LimeGreen : Brushes.IndianRed;
			}

			PropertyChangedEventHandler roleWatcher = ( s, e ) =>
			{
				if(e.PropertyName == "Role") UpdateCopierColor();
			};
			foreach(var r in Rows)
			{
				r.PropertyChanged += roleWatcher;
			}

			Rows.CollectionChanged += ( s, e ) =>
			{
				if(e.NewItems != null)
					foreach(AccountRow r in e.NewItems)
						r.PropertyChanged += roleWatcher;
				if(e.OldItems != null)
					foreach(AccountRow r in e.OldItems)
						r.PropertyChanged -= roleWatcher;
			};


			// Keep a ref to main grid
			MainGrid = dg;


			// Align main and summary grid's resizing
			for(int i = 0; i < dg.Columns.Count && i < sum.Columns.Count; i++)
			{
				var mainCol = dg.Columns[ i ];
				var sumCol = sum.Columns[ i ];

				// bind the summary column width to the main column's ActualWidth
				var b = new Binding( "ActualWidth" )
				{
					Source = mainCol,
					Mode = BindingMode.OneWay,
					Converter = new DoubleToDataGridLength()
				};
				BindingOperations.SetBinding( sumCol, DataGridColumn.WidthProperty, b );
			}


			return root;
        }


		public sealed class DoubleToDataGridLength : IValueConverter
		{
			public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
				=> (value is double d) ? new DataGridLength( d, DataGridLengthUnitType.Pixel )
									 : new DataGridLength( 0 );

			public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
				=> (value is DataGridLength l) ? l.Value : 0d;
		}


		// Helpers to build columns
		DataGridTextColumn TextReadOnlyCol( string header, string path, double width, TextAlignment halign = TextAlignment.Left )
		{
			var obj = new DataGridTextColumn
			{
				Header = header,
				Binding = string.IsNullOrEmpty( path ) ? new Binding() : new Binding( path ),
				Width = width,
				IsReadOnly = true,
				// right-align text in normal display
				ElementStyle = new Style( typeof( TextBlock ) )
				{
					Setters = { 
						new Setter( TextBlock.TextAlignmentProperty, halign ), 
						new Setter( TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center ),
						new Setter( TextBlock.PaddingProperty, new Thickness( 6, 0, 6, 0 ) )
					}
				}
			};

			return obj;
		}

		DataGridTextColumn TextCol(string header, string path, double width, bool isReadOnly = false)
        {
			var obj = new DataGridTextColumn
			{
				Header = header,
				Binding = string.IsNullOrEmpty( path ) ? new Binding() : new Binding( path ),
				Width = width,
				IsReadOnly = isReadOnly,
				// right-align text in normal display
				ElementStyle = new Style( typeof( TextBlock ) )
				{
					Setters = { new Setter( TextBlock.TextAlignmentProperty, TextAlignment.Left ), new Setter( TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center ) }
				}
			};
			return obj;
		}

		DataGridTextColumn IntEditableCol( string header, string path, int width, TextAlignment halign = TextAlignment.Left )
		{
			var col = new DataGridTextColumn
			{
				Header = header,
				Binding = new Binding( path )
				{
					Mode = BindingMode.TwoWay,
					UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
				},
				Width = width
			};
			var style = new Style( typeof( TextBlock ) );
			style.Setters.Add( new Setter( TextBlock.TextAlignmentProperty, halign ) );
			style.Setters.Add( new Setter( TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center ) );
			style.Setters.Add( new Setter( TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center ) );
			style.Setters.Add( new Setter( TextBlock.PaddingProperty, new Thickness( 6, 0, 6, 0 ) ) );
			col.ElementStyle = style;

			// Edit mode (TextBox)
			var editStyle = new Style( typeof( TextBox ) );
			editStyle.Setters.Add( new Setter( TextBox.TextAlignmentProperty, halign ) );
			editStyle.Setters.Add( new Setter( TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center ) );
			editStyle.Setters.Add( new Setter( TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center ) );
			editStyle.Setters.Add( new Setter( Control.PaddingProperty, new Thickness( 6, 0, 6, 0 ) ) );
			editStyle.Setters.Add( new Setter( Control.BackgroundProperty, new SolidColorBrush( Color.FromRgb( 40, 40, 40 ) ) ) ); // dark edit background
			editStyle.Setters.Add( new Setter( Control.ForegroundProperty, Brushes.White ) );
			editStyle.Setters.Add( new Setter( Control.BorderThicknessProperty, new Thickness( 0 ) ) );
			col.EditingElementStyle = editStyle;

			return col;
		}

		DataGridTextColumn IntCol(string header, string path, double width)
        {
            var col = new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width };
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Left));
			style.Setters.Add( new Setter( TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center ) );
			//style.Setters.Add( new Setter( TextBlock.TextAlignmentProperty, TextAlignment.Right ) );
			col.ElementStyle = style; return col;
        }
		DataGridTextColumn IntReadOnlyCol( string header, string path, double width, TextAlignment halign = TextAlignment.Left )
		{
			var col = new DataGridTextColumn { Header = header, Binding = new Binding( path ), Width = width, IsReadOnly = true };
			var style = new Style( typeof( TextBlock ) );
			style.Setters.Add( new Setter( TextBlock.TextAlignmentProperty, halign ) );
			style.Setters.Add( new Setter( TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center ) );
			//style.Setters.Add( new Setter( TextBlock.TextAlignmentProperty, TextAlignment.Right ) );
			style.Setters.Add( new Setter( TextBlock.PaddingProperty, new Thickness( 6, 0, 6, 0 ) ) );
			col.ElementStyle = style; return col;
		}

		DataGridTextColumn MoneyCol(string header, string path, bool colored)
        {
			var col = new DataGridTextColumn
			{
				Header = header,
				Binding = new Binding( path ) { StringFormat = "C2" },
				Width = 120,
				IsReadOnly = true
			};
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Left));
			style.Setters.Add( new Setter( TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center ) );
			//style.Setters.Add( new Setter( TextBlock.TextAlignmentProperty, TextAlignment.Right ) );
			style.Setters.Add( new Setter( TextBlock.PaddingProperty, new Thickness( 6, 0, 6, 0 ) ) );
			if(colored) style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new Binding(path) { Converter = new PnLBrushConverter() }));
            col.ElementStyle = style;
            return col;
        }
        DataGridTextColumn MoneyEditableCol(string header, string path)
        {
            var col = new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(path) { Mode = BindingMode.TwoWay, StringFormat = "C2" },
                Width = 120
            };
			var style = new Style( typeof( TextBlock ) );
			style.Setters.Add( new Setter( TextBlock.TextAlignmentProperty, TextAlignment.Left ) );
			style.Setters.Add( new Setter( TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center ) );
			style.Setters.Add( new Setter( TextBlock.PaddingProperty, new Thickness( 6, 0, 6, 0 ) ) );
			col.ElementStyle = style;

			// Edit mode (TextBox)
			var editStyle = new Style( typeof( TextBox ) );
			editStyle.Setters.Add( new Setter( TextBox.TextAlignmentProperty, TextAlignment.Left ) );
			editStyle.Setters.Add( new Setter( TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center ) );
			editStyle.Setters.Add( new Setter( Control.PaddingProperty, new Thickness( 4, 0, 6, 0 ) ) );
			editStyle.Setters.Add( new Setter( Control.BackgroundProperty, new SolidColorBrush( Color.FromRgb( 40, 40, 40 ) ) ) ); // dark edit background
			editStyle.Setters.Add( new Setter( Control.ForegroundProperty, Brushes.White ) );
			editStyle.Setters.Add( new Setter( Control.BorderThicknessProperty, new Thickness( 0 ) ) );
			col.EditingElementStyle = editStyle;
			return col;
        }
        DataGridTemplateColumn ButtonCol(string header, string commandPath, double width)
        {
            var tpl = new DataTemplate();
            var btn = new FrameworkElementFactory(typeof(Button));
            btn.SetValue(Button.ContentProperty, header);
            btn.SetBinding(Button.CommandProperty, new Binding(commandPath));
            tpl.VisualTree = btn;
            return new DataGridTemplateColumn { Header = header, CellTemplate = tpl, Width = width };
        }

		public sealed class RoleLabelConverter : IValueConverter
		{
			public object Convert( object value, Type t, object p, CultureInfo c )
			{
				if( value is RowRole)
				{
					var rr = (RowRole)value;
					if(rr == RowRole.Master) return "M";
					else if(rr == RowRole.Follower) return "F";
				}
				return "";
				//return value is RowRole rr ? rr == RowRole.Master ? "M" : rr == RowRole.Follower ? "F" : "" : "";
			}
			public object ConvertBack( object v, Type t, object p, CultureInfo c ) => null;
		}


		ContextMenu BuildRowContextMenu()
		{
			var cm = new ContextMenu();

			// Flatten
			var miFlatten = new MenuItem { Header = "Flatten Account" };
			miFlatten.Click += ( s, e ) =>
			{
				if(GetRowFromContext( e.OriginalSource ) is AccountRow r)
					r.FlattenCmd.Execute( null );
			};
			cm.Items.Add( miFlatten );

			cm.Items.Add( new Separator() );

			// Role assignment
			var miSetMaster = new MenuItem { Header = "Set as Master" };
			miSetMaster.Click += ( s, e ) =>
			{
				//if(GetRowFromContext( e.OriginalSource ) is AccountRow r)
				//	OnRoleButtonClicked( r ); // will assign as master if none exists

				if(GetRowFromContext( e.OriginalSource ) is not AccountRow r)
					return;

				// if there is any defined master or follower, clear all first
				bool hasRoles = Rows.Any( x => x.Role == RowRole.Master || x.Role == RowRole.Follower );
				if(hasRoles)
				{
					foreach(var x in Rows)
						x.Role = RowRole.None;
					//CopierMgr.ClearAll();
				}

				// set new master
				r.Role = RowRole.Master;
				//CopierMgr.SetMaster( r.Acct );
			};

			var miSetFollower = new MenuItem { Header = "Set as Follower" };
			miSetFollower.Click += ( s, e ) =>
			{
				if(GetRowFromContext( e.OriginalSource ) is AccountRow r)
				{
					var master = Rows.FirstOrDefault( x => x.Role == RowRole.Master );
					if(master != null && r != master)
						r.Role = RowRole.Follower;
				}
			};

			var miClearRoles = new MenuItem { Header = "Clear All Roles" };
			miClearRoles.Click += ( s, e ) => { foreach(var r in Rows) r.Role = RowRole.None; };

			cm.Items.Add( miSetMaster );
			cm.Items.Add( miSetFollower );
			cm.Items.Add( miClearRoles );

			cm.Items.Add( new Separator() );

			// Visibility helpers
			var miHide = new MenuItem { Header = "Hide Account" };
			miHide.Click += ( s, e ) =>
			{
				if(GetRowFromContext( e.OriginalSource ) is AccountRow r)
					r.Hidden = true;
				View.Refresh();
			};

			cm.Items.Add( miHide );

			return cm;
		}

		// helper to resolve the clicked row
		AccountRow GetRowFromContext( object source )
		{
			var fe = source as FrameworkElement;
			while(fe != null && fe.DataContext is not AccountRow)
				fe = System.Windows.Media.VisualTreeHelper.GetParent( fe ) as FrameworkElement;
			return fe?.DataContext as AccountRow;
		}

        T FindDataContext<T>(DependencyObject start) where T : class
        {
            var fe = start;
            while (fe != null)
            {
                if (fe is FrameworkElement f && f.DataContext is T dc) return dc;
                fe = System.Windows.Media.VisualTreeHelper.GetParent(fe);
            }
            return null;
        }
    }
}
