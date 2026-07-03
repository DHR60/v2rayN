namespace v2rayN.Desktop;

public static class ObservableExtensions
{
    public static IDisposable Subscribe<T>(this IObservable<T> observable, Action<T> onNext)
    {
        return SubscribeExtensions.Subscribe(observable, onNext);
    }
}
