namespace Sapling.Shared.Content;

/// <summary>Catalog of all 28 Indian States and 8 Union Territories with their prominent cities, ordered alphabetically.</summary>
public static class LocationCatalog
{
    private static readonly Dictionary<string, string[]> StateCitiesMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Andaman and Nicobar Islands"] =
        [
            "Diglipur", "Mayabunder", "Port Blair", "Rangat", "Other"
        ],
        ["Andhra Pradesh"] =
        [
            "Anantapur", "Eluru", "Guntur", "Kadapa", "Kakinada",
            "Kurnool", "Nellore", "Rajahmundry", "Tirupati", "Vijayawada",
            "Visakhapatnam", "Vizianagaram", "Other"
        ],
        ["Arunachal Pradesh"] =
        [
            "Itanagar", "Naharlagun", "Pasighat", "Roing", "Tawang",
            "Tezu", "Ziro", "Other"
        ],
        ["Assam"] =
        [
            "Barpeta", "Bongaigaon", "Dhubri", "Dibrugarh", "Guwahati",
            "Jorhat", "Nagaon", "Silchar", "Tezpur", "Tinsukia", "Other"
        ],
        ["Bihar"] =
        [
            "Arrah", "Begusarai", "Bhagalpur", "Bihar Sharif", "Chhapra",
            "Darbhanga", "Gaya", "Katihar", "Munger", "Muzaffarpur",
            "Patna", "Purnia", "Other"
        ],
        ["Chandigarh"] =
        [
            "Chandigarh", "Other"
        ],
        ["Chhattisgarh"] =
        [
            "Ambikapur", "Bhilai", "Bilaspur", "Durg", "Jagdalpur",
            "Korba", "Raigarh", "Raipur", "Rajnandgaon", "Other"
        ],
        ["Dadra and Nagar Haveli and Daman and Diu"] =
        [
            "Daman", "Diu", "Silvassa", "Other"
        ],
        ["Delhi (NCR)"] =
        [
            "Central Delhi", "East Delhi", "Faridabad", "Ghaziabad", "Greater Noida",
            "Gurugram", "New Delhi", "Noida", "North Delhi", "South Delhi",
            "West Delhi", "Other"
        ],
        ["Goa"] =
        [
            "Mapusa", "Margao", "Panaji", "Ponda", "Vasco da Gama", "Other"
        ],
        ["Gujarat"] =
        [
            "Ahmedabad", "Anand", "Bharuch", "Bhavnagar", "Gandhinagar",
            "Jamnagar", "Junagadh", "Morbi", "Navsari", "Rajkot",
            "Surat", "Vadodara", "Vapi", "Other"
        ],
        ["Haryana"] =
        [
            "Ambala", "Faridabad", "Gurugram", "Hisar", "Karnal",
            "Kurukshetra", "Panchkula", "Panipat", "Rohtak", "Sirsa",
            "Sonipat", "Yamunanagar", "Other"
        ],
        ["Himachal Pradesh"] =
        [
            "Baddi", "Bilaspur", "Chamba", "Dharamshala", "Hamirpur",
            "Kullu", "Mandi", "Shimla", "Solan", "Una", "Other"
        ],
        ["Jammu & Kashmir"] =
        [
            "Anantnag", "Baramulla", "Jammu", "Kathua", "Sopore",
            "Srinagar", "Udhampur", "Other"
        ],
        ["Jharkhand"] =
        [
            "Bokaro", "Deoghar", "Dhanbad", "Giridih", "Hazaribagh",
            "Jamshedpur", "Medininagar", "Ramgarh", "Ranchi", "Other"
        ],
        ["Karnataka"] =
        [
            "Ballari", "Belagavi", "Bengaluru", "Bidar", "Davanagere",
            "Hassan", "Hubballi-Dharwad", "Kalaburagi", "Mangaluru", "Mysuru",
            "Shivamogga", "Tumakuru", "Udupi", "Other"
        ],
        ["Kerala"] =
        [
            "Alappuzha", "Kannur", "Kasaragod", "Kochi", "Kollam",
            "Kottayam", "Kozhikode", "Malappuram", "Palakkad", "Thiruvananthapuram",
            "Thrissur", "Other"
        ],
        ["Ladakh"] =
        [
            "Kargil", "Leh", "Other"
        ],
        ["Lakshadweep"] =
        [
            "Agatti", "Amini", "Andrott", "Kavaratti", "Minicoy", "Other"
        ],
        ["Madhya Pradesh"] =
        [
            "Bhopal", "Burhanpur", "Dewas", "Gwalior", "Indore",
            "Jabalpur", "Katni", "Khandwa", "Morena", "Ratlam",
            "Rewa", "Sagar", "Satna", "Singrauli", "Ujjain", "Other"
        ],
        ["Maharashtra"] =
        [
            "Akola", "Amravati", "Chhatrapati Sambhajinagar", "Jalgaon", "Kolhapur",
            "Latur", "Mumbai", "Nagpur", "Nanded", "Nashik",
            "Navi Mumbai", "Pune", "Solapur", "Thane", "Other"
        ],
        ["Manipur"] =
        [
            "Bishnupur", "Churachandpur", "Imphal", "Thoubal", "Ukhrul", "Other"
        ],
        ["Meghalaya"] =
        [
            "Cherrapunji", "Jowai", "Nongpoh", "Shillong", "Tura", "Other"
        ],
        ["Mizoram"] =
        [
            "Aizawl", "Champhai", "Kolasib", "Lunglei", "Serchhip", "Other"
        ],
        ["Nagaland"] =
        [
            "Dimapur", "Kohima", "Mokokchung", "Tuensang", "Wokha", "Other"
        ],
        ["Odisha"] =
        [
            "Balasore", "Baripada", "Berhampur", "Bhadrak", "Bhubaneswar",
            "Cuttack", "Jharsuguda", "Puri", "Rourkela", "Sambalpur", "Other"
        ],
        ["Puducherry"] =
        [
            "Karaikal", "Mahe", "Ozhukarai", "Puducherry", "Yanam", "Other"
        ],
        ["Punjab"] =
        [
            "Abohar", "Amritsar", "Batala", "Bathinda", "Chandigarh",
            "Hoshiarpur", "Jalandhar", "Ludhiana", "Moga", "Mohali",
            "Pathankot", "Patiala", "Other"
        ],
        ["Rajasthan"] =
        [
            "Ajmer", "Alwar", "Bharatpur", "Bhilwara", "Bikaner",
            "Jaipur", "Jodhpur", "Kota", "Pali", "Sikar",
            "Sri Ganganagar", "Udaipur", "Other"
        ],
        ["Sikkim"] =
        [
            "Gangtok", "Geyzing", "Mangan", "Namchi", "Rangpo", "Other"
        ],
        ["Tamil Nadu"] =
        [
            "Chennai", "Coimbatore", "Dindigul", "Erode", "Kanchipuram",
            "Madurai", "Nagercoil", "Salem", "Thanjavur", "Thoothukudi",
            "Tiruchirappalli", "Tirunelveli", "Tiruppur", "Vellore", "Other"
        ],
        ["Telangana"] =
        [
            "Adilabad", "Hyderabad", "Karimnagar", "Khammam", "Mahbubnagar",
            "Nalgonda", "Nizamabad", "Ramagundam", "Siddipet", "Warangal", "Other"
        ],
        ["Tripura"] =
        [
            "Agartala", "Ambassa", "Belonia", "Dharmanagar", "Kailashahar",
            "Udaipur", "Other"
        ],
        ["Uttar Pradesh"] =
        [
            "Agra", "Aligarh", "Ayodhya", "Bareilly", "Ghaziabad",
            "Gorakhpur", "Greater Noida", "Jhansi", "Kanpur", "Lucknow",
            "Mathura", "Meerut", "Moradabad", "Muzaffarnagar", "Noida",
            "Prayagraj", "Saharanpur", "Varanasi", "Other"
        ],
        ["Uttarakhand"] =
        [
            "Dehradun", "Haldwani", "Haridwar", "Kashipur", "Nainital",
            "Pithoragarh", "Rishikesh", "Roorkee", "Rudrapur", "Other"
        ],
        ["West Bengal"] =
        [
            "Asansol", "Baharampur", "Bardhaman", "Durgapur", "Haldia",
            "Howrah", "Kharagpur", "Kolkata", "Malda", "Midnapore",
            "Siliguri", "Other"
        ]
    };

    public static IReadOnlyList<string> GetStates() => StateCitiesMap.Keys.ToList();

    public static IReadOnlyList<string> GetCitiesForState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return [];

        return StateCitiesMap.TryGetValue(state, out var cities) ? cities : ["Other"];
    }

    /// <summary>Attempts to infer the state from a known city name (useful for legacy data).</summary>
    public static string InferStateFromCity(string? city)
    {
        if (string.IsNullOrWhiteSpace(city))
            return "";

        var trimmed = city.Trim();
        foreach (var (state, cities) in StateCitiesMap)
        {
            if (cities.Any(c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                return state;
            }
        }

        return "";
    }
}
