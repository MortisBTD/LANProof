using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LANProof.Runtime.Companion;
using QRCoder;

namespace LANProof.App;

public sealed partial class MainWindow
{
    private Control Companion()
    {
        if(_companion==null)
        {
            _companion=new(_session);
            _companion.Changed+=()=>Dispatcher.UIThread.Post(()=>
            {
                if(_lifetime.IsCancellationRequested)return;
                if(_companion.PendingName==null && _companion.Device!=null)_pairingCode="";
                if(_page==5)RenderPage();else UpdateHeader();
            });
        }
        _intro.Text="Локальное подключение телефона и очередь измерений";
        var addresses=CompanionHost.LocalAddresses();
        var address=new ComboBox {ItemsSource=addresses,SelectedIndex=addresses.Count>0 ? 0 : -1,
            MinWidth=230,IsEnabled=!_companion.IsRunning,FontSize=13};
        var controls=Stack(12,Text("ПРИЁМ С ТЕЛЕФОНА",11,Muted,true),
            Text(_companion.IsRunning ? "Локальное соединение включено" : "Приём выключен",22,bold:true),
            Text("Выберите IPv4 домашней сети. Телефон и ПК должны быть в одной локальной сети. Приём включается только этой кнопкой и прекращается при закрытии LANProof.",13,Muted),address,
            Button(_companion.IsRunning ? "Выключить приём" : "Включить приём",async()=>
            {
                if(_companion.IsRunning){await _companion.StopAsync();_pairingCode="";}
                else _companion.Start(address.SelectedItem as System.Net.IPAddress ?? throw new InvalidOperationException("Нет доступного IPv4"));
                RenderPage();
            },accent:!_companion.IsRunning));
        if(_companion.IsRunning)controls.Children.Add(Button("Создать код на 5 минут",()=>
        { _pairingCode=_companion.CreateInvitation().ToCode();RenderPage();return Task.CompletedTask; }));
        var page=Stack(16,Card(controls));
        if(!string.IsNullOrEmpty(_pairingCode) && _companion.IsRunning)
        {
            using var generator=new QRCodeGenerator();using var qr=generator.CreateQrCode(_pairingCode,QRCodeGenerator.ECCLevel.M);
            using var png=new PngByteQRCode(qr);using var bytes=new MemoryStream(png.GetGraphic(5));
            var image=new Image {Source=new Bitmap(bytes),Width=200,Height=200,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left};
            var code=new TextBox {Text=_pairingCode,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,FontSize=11,CornerRadius=new CornerRadius(6),MaxHeight=120};
            page.Children.Add(Card(Stack(12,Text("Сопряжение",22,bold:true),
                Text("В Android-компаньоне нажмите «QR-код» или вставьте код ниже. Секрет подключения не публикуйте. После сканирования подтвердите запрос на этом ПК.",13,Muted),image,code,
                Button("Скопировать код",async()=>{if(Clipboard!=null)await Clipboard.SetTextAsync(_pairingCode);} ))));
        }
        if(_companion.PendingName is {} pending)
            page.Children.Add(Card(Stack(12,Text("Запрос: "+pending,22,bold:true),Text("Подтвердите только свой телефон, на котором вы только что отсканировали код.",13,Muted),
                Button("Подтвердить телефон",async()=>{await _companion.ApproveAsync();_message="Телефон подтверждён. Дождитесь завершения сопряжения на Android.";UpdateHeader();},accent:true),
                Button("Отклонить и отозвать доступ",async()=>{await _companion.RevokeAsync();_pairingCode="";RenderPage();}))));
        page.Children.Add(Card(Stack(12,Text(_companion.Device is {} device ? "Телефон: "+device.Name : "Нет сопряжённого телефона",22,bold:true),
            Text(_companion.LastSyncUtc is {} last ? "Последняя синхронизация: "+last.ToLocalTime().ToString("dd.MM HH:mm:ss") : "Измерений с телефона в этом сеансе ещё не получено",13,Muted),
            Text("TLS 1.2/1.3, закрепление сертификата и отдельный ключ устройства. На Windows ключ защищён DPAPI текущего пользователя. Отзыв доступа прекращает дальнейшую загрузку.",13,Muted),
            Button("Отозвать доступ телефона",async()=>{await _companion.RevokeAsync();_pairingCode="";RenderPage();}))));
        if(_companion.Error is {} error)page.Children.Add(Text(error,13,Red));
        page.Children.Add(Card(Stack(10,Text("Если телефон не подключается",16,bold:true),
            Text("Проверьте выбранный IPv4, одинаковую домашнюю сеть и разрешение Windows Firewall для LANProof в частной сети. Гостевой Wi-Fi и изоляция клиентов могут блокировать порт TCP 13791. Интернет, облако и проброс портов не нужны.",13,Muted),
            Text("Телефон записывает циклы локально и повторяет отправку после восстановления Wi-Fi. Повтор пакета не создаёт дубликаты. Для общего вывода требуется согласованное время; отсутствие измерения не считается исправным соединением.",13,Muted))));
        return page;
    }
}
