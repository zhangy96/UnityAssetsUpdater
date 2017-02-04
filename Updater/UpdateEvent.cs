namespace Core.Updater
{
    public class UpdateEvent
    {
        //! Update events code
        public enum EventCode
        {
            ERROR_NO_LOCAL_MANIFEST,
            ERROR_DOWNLOAD_MANIFEST,
            ERROR_PARSE_MANIFEST,
            NEW_VERSION_FOUND,
            ALREADY_UP_TO_DATE,
            UPDATE_PROGRESSION,
            ASSET_UPDATED,
            ERROR_UPDATING,
            UPDATE_FINISHED,
            UPDATE_FAILED,
            // ERROR_DECOMPRESS
        }

        public EventCode Code { get; private set; }

        public string AssetId { get; private set; }

        public string Message { get; private set; }

        public int PercentByFile { get; private set; }

        public AssetsUpdater Updater { get; private set; }

        public UpdateEvent(AssetsUpdater updater, EventCode code, string assetId, int percentByFile, string message)
        {
            Updater = updater;
            Code = code;
            AssetId = assetId;
            Message = message;
            PercentByFile = percentByFile;
        }
    }
}