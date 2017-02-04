# AssetsUpdater - AssetsManagerEx for unity3d

`Unity3d`资源热更器，基于`Cocos2dx AssetsManagerEx`，用法与`Cocos2dx AssetsManagerEx`一致

##Usage:
```cs
private void Start()
{
    var updater = new AssetsUpdater(Application.streamingAssetsPath + "/project.manifest", Application.persistentDataPath + "/");
    updater.OnUpdateEvent += OnUpdateEvent;
    updater.CheckUpdate();
}

private void OnUpdateEvent(UpdateEvent updateEvent)
{
    Debug.Log(updateEvent.Code.ToString());
    switch (updateEvent.Code)
    {
        case UpdateEvent.EventCode.ERROR_NO_LOCAL_MANIFEST:
            break;
        case UpdateEvent.EventCode.ERROR_DOWNLOAD_MANIFEST:
            break;
        case UpdateEvent.EventCode.ERROR_PARSE_MANIFEST:
            break;
        case UpdateEvent.EventCode.NEW_VERSION_FOUND:
            updateEvent.Updater.StartUpdate();
            break;
        case UpdateEvent.EventCode.ALREADY_UP_TO_DATE:
        case UpdateEvent.EventCode.UPDATE_FINISHED:
            AfterUpdate();
            break;
        case UpdateEvent.EventCode.UPDATE_PROGRESSION:
            break;
        case UpdateEvent.EventCode.ASSET_UPDATED:
            break;
        case UpdateEvent.EventCode.ERROR_UPDATING:
            break;
        case UpdateEvent.EventCode.UPDATE_FAILED:
            break;
        default:
            throw new ArgumentOutOfRangeException();
    }
}

private void AfterUpdate()
{
}
```
