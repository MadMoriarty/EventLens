using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using EventLens.Core;
namespace EventLens.App;
public sealed class LiveRowColor : IValueConverter
{
    public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture)=>value is EventItem {IsLive:true,Category:EventCategory.Lockout}?Brush.Parse("#33202C"):value is EventItem {IsLive:true,Category:EventCategory.FailedAuthentication}?Brush.Parse("#2C2426"):Brushes.Transparent;
    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>throw new NotSupportedException();
}
public sealed class SeverityColor : IValueConverter
{
    public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture)=>Brush.Parse(value is EventItem {Category:EventCategory.Lockout}?"#F28CA9":(value is EventItem item?item.Severity:value?.ToString()) switch {"Critical"=>"#F28CA9","Error" or "Audit failure"=>"#F19A91","Warning"=>"#F3A35C","Audit success"=>"#83C9DE","Information"=>"#BECBD8",_=>"#9CA3B4"});
    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>throw new NotSupportedException();
}
