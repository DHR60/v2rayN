using ServiceLib.Enums;
using ServiceLib.ViewModels;

namespace v2rayN.Web.Maneger;

public class SingletonManager
{
    public static SingletonManager Instance => field ??= new SingletonManager();

    #region MsgViewModel Singleton

    public MsgViewModel MsgViewModel { get; set; }

    private readonly ReaderWriterLockSlim _rwLock = new();
    private string _msgViewContent = string.Empty;

    public SingletonManager()
    {
        MsgViewModel = new MsgViewModel(MsgViewModelUpdateViewHandler);
    }

    public string MsgViewContent
    {
        get
        {
            _rwLock.EnterReadLock();
            try
            { return _msgViewContent; }
            finally { _rwLock.ExitReadLock(); }
        }
        set
        {
            _rwLock.EnterWriteLock();
            try
            { _msgViewContent = value; }
            finally { _rwLock.ExitWriteLock(); }
        }
    }

    public void Append(string msg)
    {
        _rwLock.EnterWriteLock();
        try
        { _msgViewContent += msg; }
        finally { _rwLock.ExitWriteLock(); }
    }

    public void Clear()
    {
        _rwLock.EnterWriteLock();
        try
        { _msgViewContent = "----- Message cleared -----\n"; }
        finally { _rwLock.ExitWriteLock(); }
    }

    private async Task<bool> MsgViewModelUpdateViewHandler(EViewAction action, object? obj)
    {
        switch (action)
        {
            case EViewAction.DispatcherShowMsg:
                if (obj is null)
                {
                    return false;
                }
                ShowMsg(obj);
                break;
        }
        return await Task.FromResult(true);
    }

    public void ShowMsg(object msg)
    {
        var str = msg?.ToString();
        if (str == null)
        {
            return;
        }
        if (MsgViewContent.Count('\n') > (MsgViewModel?.NumMaxMsg ?? 100))
        {
            Clear();
        }
        Append(str);
    }

    #endregion MsgViewModel Singleton
}
