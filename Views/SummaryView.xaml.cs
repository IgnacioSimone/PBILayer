using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using PBIExplorer.ViewModels;

namespace PBIExplorer.Views;

public partial class SummaryView : UserControl
{
    public SummaryView() => InitializeComponent();

    private void Tile_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border card || card.DataContext is not TileViewModel tile) return;

        // Entrada escalonada: cada tarjeta aparece un poco después que la anterior.
        var delay = TimeSpan.FromMilliseconds(Math.Min(tile.Order, 10) * 35);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // Transform y sombra propios de cada tarjeta, creados acá: los que se declaran en una
        // plantilla XAML WPF los congela y no se pueden animar (y el Style "Card" comparte la
        // sombra entre todas, así que el hover movería todas a la vez).
        card.RenderTransform = new TranslateTransform();
        card.Effect = new DropShadowEffect
        {
            Color = Colors.Black, Opacity = 0.05, BlurRadius = 18, ShadowDepth = 3, Direction = 270
        };

        card.Opacity = 0;
        card.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = 0, To = 1, BeginTime = delay,
            Duration = TimeSpan.FromMilliseconds(260), EasingFunction = ease
        });

        if (card.RenderTransform is TranslateTransform tt)
        {
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
            {
                From = 10, To = 0, BeginTime = delay,
                Duration = TimeSpan.FromMilliseconds(280), EasingFunction = ease
            });
        }
    }

    // Elevación sutil al pasar el mouse. Solo se traslada (sin reescalar) y se anima la
    // sombra propia de la tarjeta: escalar obligaría a WPF a resamplear el texto ya
    // rasterizado y se vería borroso.
    private void Tile_MouseEnter(object sender, MouseEventArgs e) => Lift(sender, -3, 26, 0.12, 6);

    private void Tile_MouseLeave(object sender, MouseEventArgs e) => Lift(sender, 0, 18, 0.05, 3);

    private static void Lift(object sender, double toY, double toBlur, double toOpacity, double toDepth)
    {
        if (sender is not Border { RenderTransform: TranslateTransform translate } card) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(160);
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(toY, duration) { EasingFunction = ease });

        if (card.Effect is not DropShadowEffect shadow) return;
        shadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(toBlur, duration));
        shadow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(toOpacity, duration));
        shadow.BeginAnimation(DropShadowEffect.ShadowDepthProperty, new DoubleAnimation(toDepth, duration));
    }
}
