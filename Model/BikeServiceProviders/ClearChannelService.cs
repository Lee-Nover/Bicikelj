﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using ServiceStack.Text;

namespace Bicikelj.Model
{
    public class ClearChannelService : BikeServiceProvider
    {
        public static ClearChannelService Instance = new ClearChannelService();

        private static string StationListUrl = "https://{0}/formmap/getJsonObject";

        public class BicingCommand
        {
            public string command { get; set; }
            public string method { get; set; }
            public string data { get; set; }
        }

        public class BicingAvailability
        {
            public int StationID { get; set; }
            public string StationName { get; set; }
            public double AddressGmapsLongitude { get; set; }
            public double AddressGmapsLatitude { get; set; }
            public int StationAvailableBikes { get; set; }
            public int StationFreeSlot { get; set; }
            public string AddressStreet1 { get; set; }
            public string AddressNumber { get; set; }
            public string StationStatusCode { get; set; }
        }

        private static List<StationAndAvailability> LoadStationsFromHTML(string stationsStr, string cityName)
        {
            if (string.IsNullOrWhiteSpace(stationsStr))
                return null;
            List<StationAndAvailability> stations = new List<StationAndAvailability>();

            const string CDataStr = @"""data"":""[{";
            const string CDataEndStr = @"}]"",""";
            var dataStart = stationsStr.IndexOf(CDataStr);
            if (dataStart > 0)
            {
                dataStart += CDataStr.Length - 2;
                var dataEnd = stationsStr.IndexOf(CDataEndStr, dataStart);
                if (dataEnd > 0)
                {
                    stationsStr = stationsStr.Substring(dataStart, dataEnd - dataStart + 2);
                }
            }
            
            stationsStr = Regex.Unescape(stationsStr);
            List<BicingAvailability> availabilityList = stationsStr.FromJson<List<BicingAvailability>>();
            foreach (var item in availabilityList)
            {
                if (string.IsNullOrEmpty(item.AddressStreet1))
                    continue;

                var station = new StationLocation();
                station.Name = item.StationName;
                station.Number = item.StationID;
                station.Latitude = item.AddressGmapsLatitude;
                station.Longitude = item.AddressGmapsLongitude;
                station.Open = item.StationStatusCode == "OPN";
                station.Address = item.AddressStreet1;
                station.City = cityName;
                if (!string.IsNullOrEmpty(item.AddressNumber))
                    station.Address += " " + item.AddressNumber;

                var availability = new StationAvailability();
                availability.Available = item.StationAvailableBikes;
                availability.Free = item.StationFreeSlot;
                availability.Total = availability.Available + availability.Free;
                availability.Open = station.Open;

                stations.Add(new StationAndAvailability(station, availability));
            }
            return stations;
        }

        protected override IList<City> GetCities()
        {
            var result = new List<City>() {
                new City(){ CityName = "Barcelona", Country = "Spain", ServiceName = "bicing", UrlCityName = "www.bicing.cat/ca", Provider = Instance },
                new City(){ CityName = "Zaragoza", Country = "Spain", ServiceName = "bizi Zaragoza", UrlCityName = "www.bizizaragoza.com/es", Provider = Instance }
            };
            return result;
        }

        public override IObservable<List<StationAndAvailability>> DownloadStationsWithAvailability(string cityName)
        {
            var url = string.Format(StationListUrl, cityName);
            return DownloadUrl.GetAsync(url)
                .Select(cmdList =>
                {
                    var sl = LoadStationsFromHTML(cmdList, cityName);
                    UpdateAvailabilityCache(sl);
                    return sl;
                });
        }

        public override IObservable<StationAndAvailability> GetAvailability2(StationLocation station)
        {
            var availability = GetAvailabilityFromCache(station);
            if (availability.Availability != null)
                return Observable.Return<StationAndAvailability>(availability);
            else
                return DownloadStationsWithAvailability(station.City)
                    .Select(sl => sl.Where(sa => sa.Station.Number == station.Number).FirstOrDefault());
        }
    }
}