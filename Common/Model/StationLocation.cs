﻿using System.Device.Location;
using System.Runtime.Serialization;

namespace Bicikelj.Model
{
    public class StationLocation
    {
        public int Number { get; set; }
        public string Name { get; set; }
        public string Address{ get; set; }
        public string FullAddress { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public bool Open { get; set; }
        public bool IsFavorite { get; set; }

        private GeoCoordinate coordinate;
        [IgnoreDataMember]
        public GeoCoordinate Coordinate
        {
            get {
                if (coordinate == null)
                    coordinate = new GeoCoordinate(Latitude, Longitude);
                return coordinate;
            }
            set {
                coordinate = value;
                Latitude = coordinate.Latitude;
                Longitude = coordinate.Longitude;
            }
        }

        public string City { get; set; }

        public StationLocation()
        {
            Open = true;
        }
    }
}
