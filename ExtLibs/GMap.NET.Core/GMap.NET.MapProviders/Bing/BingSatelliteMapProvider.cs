
namespace GMap.NET.MapProviders
{
   using System;

   /// <summary>
   /// BingSatelliteMapProvider provider
   /// </summary>
   public class BingSatelliteMapProvider : BingMapProviderBase
   {
      public static readonly BingSatelliteMapProvider Instance;

      BingSatelliteMapProvider()
      {  
      }

      static BingSatelliteMapProvider()
      {
         Instance = new BingSatelliteMapProvider();
      }

      private int initializationStarted;

      public override void OnInitialized()
      {
         // 首次切换底图不在界面线程等待版本或凭据查询，仍复用原初始化逻辑。
         if (System.Threading.Interlocked.Exchange(ref initializationStarted, 1) == 0)
            System.Threading.Tasks.Task.Run(() => base.OnInitialized());
      }
      #region GMapProvider Members

      readonly Guid id = new Guid("3AC742DD-966B-4CFB-B67D-33E7F82F2D37");
      public override Guid Id
      {
         get
         {
            return id;
         }
      }

      readonly string name = Resources.Strings.BingSatelliteMap;
      public override string Name
      {
         get
         {
            return name;
         }
      }

      public override PureImage GetTileImage(GPoint pos, int zoom)
      {
         string url = MakeTileImageUrl(pos, zoom, LanguageStr);

         return GetTileImageUsingHttp(url);
      }

      #endregion

      string MakeTileImageUrl(GPoint pos, int zoom, string language)
      {
         string key = TileXYToQuadKey(pos.X, pos.Y, zoom);
         return string.Format(UrlFormat, GetServerNum(pos, 4), key, Version, language, (!string.IsNullOrEmpty(ClientKey) ? "&key=" + ClientKey : string.Empty));
      }

      // http://ecn.t1.tiles.virtualearth.net/tiles/a12030003131321231.jpeg?g=875&mkt=en-us&n=z

      static readonly string UrlFormat = "https://ecn.t{0}.tiles.virtualearth.net/tiles/a{1}.jpeg?g={2}&mkt={3}&n=z{4}";
   }
}
