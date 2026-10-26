using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// Transit routing over the reviewed public route table read from Zetaris (local or cloud).
/// Uses stop sequences, ride times and headways; no TfNSW key needed. Waits are half a headway, never invented as zero.
public static class ZetarisTransit
{
    const double WalkMetersPerMinute = 80, DetourFactor = 1.3, MaxAccessMeters = 1200, MaxTransferMeters = 300;
    sealed record Stop(string Id,string Name,double Lat,double Lng,string Platform,double Minutes,double Meters);
    sealed record Route(string Line,string Mode,string Agency,int Headway,int Start,int End,Stop[] Stops);
    sealed record Ride(Route Route,int From,int To)
    {
        public Stop Board=>Route.Stops[From];public Stop Alight=>Route.Stops[To];
        public double Minutes=>Math.Abs(Alight.Minutes-Board.Minutes);public double Meters=>Math.Abs(Alight.Meters-Board.Meters);
        public double Wait=>Route.Headway/2.0;
    }

    static double Haversine(double lat1,double lng1,double lat2,double lng2){
        const double R=6371000;double Rad(double d)=>d*Math.PI/180;
        var a=Math.Pow(Math.Sin(Rad(lat2-lat1)/2),2)+Math.Cos(Rad(lat1))*Math.Cos(Rad(lat2))*Math.Pow(Math.Sin(Rad(lng2-lng1)/2),2);
        return 2*R*Math.Asin(Math.Sqrt(a));
    }
    static double WalkMeters(double lat1,double lng1,double lat2,double lng2)=>Haversine(lat1,lng1,lat2,lng2)*DetourFactor;
    static Route[] Parse(JsonArray rows)=>rows.OfType<JsonObject>().Select(r=>new Route(r.Str("line"),r.Str("mode"),r.Str("agency"),Math.Max(1,r.Int("headwayMinutes",10)),r.Int("operatingStartMinute",0),r.Int("operatingEndMinute",1440),
        (r.Array("stops")??throw new ServiceException("Zetaris 公交路線資料格式無效。")).OfType<JsonObject>().Select(s=>new Stop(s.Str("id"),s.Str("name"),s.Number("lat"),s.Number("lng"),s.Str("platform"),s.Number("cumulativeMinutes"),s.Number("cumulativeMeters"))).ToArray()))
        .Where(r=>r.Stops.Length>=2&&r.Mode is "rail" or "metro" or "tram" or "ferry" or "bus").ToArray();

    public static IReadOnlyList<JourneyStep> Plan(JsonArray rows,JourneyPoint from,JourneyPoint to,DateTimeOffset departure)
    {
        var routes=Parse(rows);var zone=TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");
        var local=TimeZoneInfo.ConvertTime(departure,zone);var minuteOfDay=local.Hour*60+local.Minute;
        var open=routes.Where(r=>minuteOfDay>=r.Start&&minuteOfDay<=r.End).ToArray();
        if(open.Length==0)throw new ServiceException("NSW 此区间或日期没有可用班次。");
        double Access(Stop s)=>WalkMeters(from.Lat,from.Lng,s.Lat,s.Lng);double Egress(Stop s)=>WalkMeters(s.Lat,s.Lng,to.Lat,to.Lng);
        IEnumerable<Ride> Rides(Route r,Func<Stop,bool> boardOk,Func<Stop,bool> alightOk){
            for(var i=0;i<r.Stops.Length;i++)if(boardOk(r.Stops[i]))
                for(var j=0;j<r.Stops.Length;j++)if(j!=i&&alightOk(r.Stops[j]))yield return new Ride(r,i,j);
        }
        (double cost,Ride[] rides)? best=null;
        void Consider(double cost,params Ride[] rides){if(best is null||cost<best.Value.cost)best=(cost,rides);}
        // Direct journeys: access walk + wait + ride + egress walk.
        foreach(var r in open)foreach(var ride in Rides(r,s=>Access(s)<=MaxAccessMeters,s=>Egress(s)<=MaxAccessMeters))
            Consider(Access(ride.Board)/WalkMetersPerMinute+ride.Wait+ride.Minutes+Egress(ride.Alight)/WalkMetersPerMinute,ride);
        // One transfer only when it can help; the transfer needs a nearby interchange and another wait.
        var firsts=open.SelectMany(r=>Rides(r,s=>Access(s)<=MaxAccessMeters,_=>true)).ToArray();
        foreach(var first in firsts){
            foreach(var r in open)if(r!=first.Route)foreach(var second in Rides(r,s=>Haversine(first.Alight.Lat,first.Alight.Lng,s.Lat,s.Lng)<=MaxTransferMeters,s=>Egress(s)<=MaxAccessMeters)){
                var transferWalk=WalkMeters(first.Alight.Lat,first.Alight.Lng,second.Board.Lat,second.Board.Lng)/WalkMetersPerMinute;
                Consider(Access(first.Board)/WalkMetersPerMinute+first.Wait+first.Minutes+transferWalk+second.Wait+second.Minutes+Egress(second.Alight)/WalkMetersPerMinute,first,second);
            }
        }
        if(best is null)throw new ServiceException("NSW 未找到大众运输班次，可改选步行。");
        var steps=new List<JourneyStep>();var cursor=departure;
        void Walk(string a,double aLat,double aLng,string b,double bLat,double bLng){
            var meters=WalkMeters(aLat,aLng,bLat,bLng);if(meters<15)return;var seconds=meters/WalkMetersPerMinute*60;
            steps.Add(new("walk",null,null,a,b,cursor,cursor.AddSeconds(seconds),false,null,Math.Round(meters),seconds,0,null,null,[new(aLat,aLng),new(bLat,bLng)]));
            cursor=cursor.AddSeconds(seconds);
        }
        var previousName=from.Name;var previousLat=from.Lat;var previousLng=from.Lng;
        foreach(var ride in best.Value.rides){
            Walk(previousName,previousLat,previousLng,ride.Board.Name,ride.Board.Lat,ride.Board.Lng);
            var wait=ride.Wait*60;var start=cursor.AddSeconds(wait);var seconds=ride.Minutes*60;var step=ride.To>ride.From?1:-1;
            var geometry=new List<JourneyCoordinate>();for(var i=ride.From;i!=ride.To+step;i+=step)geometry.Add(new(ride.Route.Stops[i].Lat,ride.Route.Stops[i].Lng));
            steps.Add(new(ride.Route.Mode,ride.Route.Line,ride.Route.Stops[ride.To>ride.From?^1:0].Name,ride.Board.Name,ride.Alight.Name,start,start.AddSeconds(seconds),false,Math.Abs(ride.To-ride.From),Math.Round(ride.Meters),seconds,wait,
                string.IsNullOrWhiteSpace(ride.Board.Platform)?null:ride.Board.Platform,ride.Route.Agency,geometry));
            cursor=start.AddSeconds(seconds);previousName=ride.Alight.Name;previousLat=ride.Alight.Lat;previousLng=ride.Alight.Lng;
        }
        Walk(previousName,previousLat,previousLng,to.Name,to.Lat,to.Lng);
        return steps;
    }
}
