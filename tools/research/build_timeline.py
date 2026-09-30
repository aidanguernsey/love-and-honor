"""Record of how data/timeline.json was first compiled (2026-09-30) from researched Miami University building dates.

data/timeline.json is now edited by hand; this script is kept so the research and its sources stay traceable
(see docs/research/BUILDING_DATES.md). By default it writes tools/research/_out/timeline.json and says whether it
matches data/timeline.json; pass --write to overwrite data/timeline.json (this discards hand edits).

    tools\\.venv\\Scripts\\python tools\\research\\build_timeline.py [--write]

osm_named.json: named buildings from the map pipeline's OpenStreetMap extract (name -> OSM id, tile);
© OpenStreetMap contributors, ODbL.
"""
import json, pathlib, re, sys

REPO = next(p for p in pathlib.Path(__file__).resolve().parents if (p / "project.godot").exists())
OSM = json.load(open(pathlib.Path(__file__).with_name("osm_named.json"), encoding="utf-8"))
OSM_BY_NAME = {}
for o in OSM:
    OSM_BY_NAME.setdefault(o["name"], o["osm_id"])

TL = "https://miamioh.edu/about-miami/history-traditions/timeline/"
SOURCES = {
    "mu_tl_old": ("Miami University Historical Timeline: Old Miami, 1787–1885", TL + "old-miami/index.html", "Miami University"),
    "mu_tl_new": ("Miami University Historical Timeline: New Miami, 1885–1941", TL + "new-miami/index.html", "Miami University"),
    "mu_tl_national": ("Miami University Historical Timeline: National University, 1941–1970", TL + "national-u/index.html", "Miami University"),
    "mu_tl_publicivy": ("Miami University Historical Timeline: Public Ivy, 1970–1996 (incl. Western College buildings acquired 1974)", TL + "public-ivy/index.html", "Miami University"),
    "mu_tl_corporate": ("Miami University Historical Timeline: Corporate University, 1996–2009", TL + "corporate-u/index.html", "Miami University"),
    "nrhp_elliott_stoddard": ("National Register of Historic Places: Elliott and Stoddard Halls", "https://npgallery.nps.gov/AssetDetail/a7b13246-7671-41d8-b627-54f745e35788", "National Park Service"),
    "oxford_walking_tour": ("Walking Tour of Oxford's University Historic District (2008, rev. 2014)", "https://enjoyoxford.org/wp-content/uploads/2023/11/walking_tour_of_oxfords_university_historic_district.pdf", "Smith Library of Regional History / Enjoy Oxford"),
    "mu_news_2013": ("Newly designed, efficient and flexible buildings welcome students to the new school year", "https://miamioh.edu/news/campus-news/2013/08/construction.html", "Miami University News, 2013-08-19"),
    "mu_news_2014": ("Ten new things Miami University students will see this semester", "https://www.miamioh.edu/news/campus-news/2014/08/what-is-new.html", "Miami University News, 2014-08-21"),
    "mu_news_2015": ("Ten new things Miami University students will see this school year", "https://miamioh.edu/news/campus-news/2015/08/what-is-new.html", "Miami University News, 2015-08-20"),
    "mu_news_2018": ("New and renovated residence halls offer places to gather and study", "https://miamioh.edu/news/campus-news/2018/08/residence-halls-2018.html", "Miami University News, 2018-08-21"),
    "mu_news_2024": ("New buildings, milestones and more highlight Miami academic year", "https://miamioh.edu/news/2024/05/new-buildings-milestones-and-more-highlight-miami-academic-year.html", "Miami University News, 2024-05"),
    "mu_news_2023_hodge": ("Newly named residence hall honors David and Valerie Hodge", "https://miamioh.edu/news/2023/09/newly-named-residence-hall-honors-david-and-valerie-hodge.html", "Miami University News, 2023-09"),
    "ms_2023_hodge": ("Dormitory's new name honors former Miami president", "https://miamistudent.net/article/2023/09/dormitorys-new-name-honors-former-miami-president", "The Miami Student, 2023-09"),
    "mu_young_hall": ("Young Hall (residence hall profile)", "https://miamioh.edu/profiles/finance-business-services/campus-services-center/residence-halls/young-hall.html", "Miami University"),
    "mu_hepburn_hall": ("Hepburn Hall (residence hall profile)", "https://miamioh.edu/profiles/campus-services-center/residence-halls/hepburn-hall.html", "Miami University"),
    "mu_marcum_hall": ("Marcum Hall (residence hall profile)", "https://miamioh.edu/profiles/campus-services-center/residence-halls/marcum-hall.html", "Miami University"),
    "ms_2021_presidents": ("Presidents Hall renaming", "https://www.miamistudent.net/article/2021/04/presidents-hall-renaming", "The Miami Student, 2021-04"),
    "mu_news_2021_ncw": ("Nellie Craig Walker Hall dedication", "https://miamioh.edu/news/2021/02/nellie-craig-walker-hall-dedication.html", "Miami University News, 2021-02"),
    "ms_2025_women": ("The women behind the buildings: Miami's architectural history", "https://miamistudent.net/article/2025/03/the-women-behind-the-buildings-miamis-architectural-history", "The Miami Student, 2025-03"),
    "ms_2016_chestnut": ("New rec center satellite to open", "https://www.miamistudent.net/article/2016/01/new-rec-center-satellite-to-open", "The Miami Student, 2016-01"),
    "jn_apc": ("Miami University opens $25M Athletic Performance Center", "https://www.journal-news.com/news/miami-university-opens-25m-athletic-performance-center/dk2xIdLw88XKwj8R6VtfVK/", "Journal-News"),
    "aia_geothermal": ("Miami University – Geothermal Energy Plant (SHP)", "https://aiaohio.secure-platform.com/a/gallery/rounds/14/details/3307", "AIA Ohio"),
    "mu_dining_dcsc": ("Dining Services (Demske Culinary Support Center)", "https://events.miamioh.edu/group/dining_services", "Miami University"),
    "wcpo_2026": ("Demolished buildings, new student rec fields part of Miami campus changes", "https://www.wcpo.com/news/local-news/butler-county/oxford/demolished-buildings-new-student-rec-fields-part-of-miami-campus-changes", "WCPO, 2026"),
    "oxfreepress_2025": ("Old buildings, new developments and alternative visions", "https://www.oxfreepress.com/miami-oxford-town-gown-developments-arena/", "Oxford Free Press, 2025-03-17"),
    "mu_parking_2016": ("2016–17 Miami parking map ('Withrow Court Demolition')", "https://miamioh.edu/_files/documents/parking/2016-17-parking-map.pdf", "Miami University"),
    "mu_campus_map": ("Miami University Oxford campus map", "https://miamioh.edu/emss/_files/documents/career-services/pdfs/CampusMap.pdf", "Miami University"),
    "ghmchs_mcguffey": ("McGuffey Hall", "https://ghmchs.org/mcguffey-hall", "ghmchs.org"),
}
WIKI = {}


def wp(title):
    sid = "wp_" + re.sub(r"[^a-z0-9]+", "_", title.lower()).strip("_")
    WIKI[sid] = (f"Wikipedia: {title}", "https://en.wikipedia.org/wiki/" + title.replace(" ", "_"), "Wikipedia")
    return sid


E = []


APPROX = {  # buildings with no outline of their own → footprint of the later building on the same site (per the cited sources)
    "old_main": "Harrison Hall", "withrow_court": "Withrow Hall", "fisher_hall_old": "The Marcum Hotel & Conference Center",
    "reid_hall_old": "Farmer School of Business", "hepburn_hall_first": "King Library",
}


def B(id, name, kind, campus, built, prec, demol, status, conf, src, notes="", other=None, events=None, osm=None, bdef=None):
    """osm: None = try exact name; str = OSM name to match; False = no present-day footprint."""
    e = {"id": id, "name": name}
    if other: e["other_names"] = other
    e["kind"] = kind
    e["campus"] = campus
    if bdef: e["building_def"] = bdef
    e["built_year"] = built
    e["built_precision"] = prec if built is not None else "unknown"
    e["demolished_year"] = demol
    e["status"] = status
    if events: e["events"] = [{"year": y, "event": t} for y, t in events]
    e["confidence"] = conf
    e["verified"] = False
    e["sources"] = src
    if notes: e["notes"] = notes
    osm_id = None
    if osm is not False:  # demolished buildings too: OSM can still hold their outline (e.g. 2026 demolitions)
        osm_id = OSM_BY_NAME.get(osm if isinstance(osm, str) else name)
        if osm_id is None and isinstance(osm, str):
            raise SystemExit(f"OSM name not found: {osm}")
    e["osm_id"] = osm_id
    if id in APPROX:
        e["approx_site_osm_id"] = OSM_BY_NAME[APPROX[id]]
        e["notes"] = (e.get("notes", "") + f" Drawn on the footprint of {APPROX[id]} (same site; approximate outline).").strip()
    e["site"] = None
    E.append(e)


T_O, T_N, T_NU, T_P, T_C = "mu_tl_old", "mu_tl_new", "mu_tl_national", "mu_tl_publicivy", "mu_tl_corporate"
WT = "oxford_walking_tour"

# ---------------- Pre-1900: Miami's original campus and University Square houses ----------------
B("dewitt_log_homestead", "DeWitt Log Homestead", "house", "outlying", 1805, "year", None, "standing", "sourced",
  [wp("Dewitt Log Homestead")], "Oldest extant structure in Oxford Township (NRHP 1973). Miami ownership / campus status unverified.", osm="Dewitt Cabin")
B("old_main", "Old Main", "academic", "main", 1818, "year", 1958, "demolished", "corroborated", [T_O, T_NU, wp("Harrison Hall")],
  "Construction began 1816; central part completed 1818. Replaced on the same site by Harrison Hall (1960).",
  other=["Franklin Hall", "Harrison Hall (original)"], events=[(1816, "construction begun"), (1868, "west wing added"), (1898, "east wing added"), (1958, "demolished")], osm=False)
B("elliott_hall", "Elliott Hall", "residence", "main", 1828, "year", None, "standing", "conflicting", [T_O, wp("Elliott and Stoddard Halls"), "nrhp_elliott_stoddard"],
  "Miami's timeline: 'constructed' 1828. Wikipedia: built 1825. NRHP lists 1829 as a significant year. First residence hall (North Hall).",
  other=["North Hall"], events=[(1937, "renovated in neo-Georgian style")], bdef="historic_hall")
B("stoddard_hall", "Stoddard Hall", "residence", "main", 1836, "year", None, "standing", "conflicting", [T_O, wp("Elliott and Stoddard Halls"), "nrhp_elliott_stoddard"],
  "Miami's timeline and Wikipedia: 1836. NRHP lists 1835 as a significant year. Second residence hall (South Hall).",
  other=["South Hall"], events=[(1937, "renovated in neo-Georgian style")], bdef="historic_hall")
B("scott_house", "Scott House (2 South Campus Avenue)", "house", "uptown_edge", 1831, "year", None, "standing", "sourced", [WT],
  "Birthplace of Caroline Scott Harrison. Part of Miami's cottage system in the mid-1920s; current Miami ownership unverified.", osm=False)
B("mcguffey_house", "William H. McGuffey House", "house", "main", 1833, "approximate", None, "standing", "corroborated", [wp("William H. McGuffey House"), WT],
  "Walking tour: ca. 1832–33. Museum; National Historic Landmark.", osm="McGuffey House and Museum")
B("bishop_house", "Bishop House (400 East High Street)", "house", "uptown_edge", 1834, "year", 1960, "demolished", "sourced", [WT],
  "Home of Miami's first president Robert Hamilton Bishop 1836–45. Acquired by Miami 1929; razed 1960.", osm=False)
B("simpson_shade_guest_house", "Simpson-Shade Guest House", "house", "uptown_edge", 1836, "approximate", None, "standing", "sourced", [WT],
  "Built about 1836 (Rogers House). Given to Miami 1930; renamed Simpson-Shade 2004.", other=["Rogers House", "Simpson Guest House"],
  events=[(1930, "given to Miami"), (2004, "renamed Simpson-Shade Guest House")], osm="Simpson Shade Guest House")
B("lewis_place", "Lewis Place", "house", "uptown_edge", 1839, "year", None, "standing", "sourced", [WT],
  "Leased to Miami 1903, sold to Miami 1929; official residence of Miami presidents. Largest addition 2007.",
  events=[(1903, "leased to Miami"), (1929, "sold to Miami"), (2007, "largest addition")])
B("kennedy_house", "Kennedy House (322 East High Street)", "house", "uptown_edge", 1839, "approximate", 1978, "demolished", "sourced", [WT],
  "'Probably constructed by 1839'. Purchased by Miami about a decade before it was destroyed by arson in 1978.", osm=False)
B("richey_house", "Richey House (220 East High Street)", "house", "uptown_edge", 1845, "approximate", None, "standing", "estimate", [WT],
  "Walking tour: 'beginning in the mid-1840s'. Given to Miami 1988; used as housing for administrators.", osm=False)
B("oxford_college", "Oxford College for Women (Oxford Female Institute building)", "academic", "oxford_college", 1850, "year", None, "standing", "sourced",
  [wp("Oxford College for Women"), T_N], "Acquired by Miami 1928 and renovated; now the Oxford Community Arts Center (leased/transferred; current ownership unverified).",
  events=[(1928, "acquired by Miami; main building renovated")], osm="Oxford Community Arts Center")
B("old_manse", "Old Manse", "house", "uptown_edge", 1852, "year", None, "standing", "corroborated", [WT, wp("Old Manse (Miami University)"), T_P],
  "'Coffee Mill House'. Presbyterian parsonage 1883–1956; Miami property from 1973 (Wikipedia).", events=[(1973, "sold to Miami")])
B("peabody_hall", "Peabody Hall", "residence", "western", 1871, "year", None, "standing", "corroborated", [wp("Peabody Hall (Miami University)"), T_P],
  "Original Seminary Hall built 1855, burned 1860, rebuilt 1861, burned 1871 and rebuilt the same year; named Peabody Hall 1905. Miami's timeline gives '1860/1871'.",
  other=["Seminary Hall"], events=[(1855, "Seminary Hall built"), (1860, "burned"), (1861, "rebuilt"), (1905, "renamed Peabody Hall"), (1974, "acquired by Miami")])
B("langstroth_cottage", "Langstroth Cottage", "house", "western", 1856, "year", None, "standing", "conflicting", [T_P, wp("Langstroth Cottage")],
  "Miami's timeline and Wikipedia text: 1856; Wikipedia infobox: 1858. National Historic Landmark (1976).")
B("bonham_house", "Bonham House", "house", "main", 1868, "year", None, "standing", "corroborated", [WT, "oxfreepress_2025"],
  "Built for Miami president Robert Stanton. Home of the Myaamia Center.")
B("brice_hall", "Brice Scientific Hall", "academic", "main", 1892, "year", None, "unknown", "sourced", [T_N],
  "Constructed for science instruction. Later history / demolition not found.", other=["Brice Hall"], osm=False)
B("alumnae_hall", "Alumnae Hall (Western College)", "library", "western", 1892, "year", 1977, "demolished", "corroborated", [T_P, wp("Alumnae Hall (Western College for Women)")],
  "Western's library until 1970; scheduled to be razed 1975, torn down 1977.", osm=False)
B("tenney_gateway", "Tenney Gateway", "landmark", "western", 1890, "decade", None, "standing", "estimate", [T_P], "Miami's timeline: '1890s'.")
B("miami_field", "Miami Field", "athletics", "main", 1896, "year", 1982, "demolished", "corroborated", [T_N, wp("Miami Field")],
  "Opened 1896 as Athletic Park (Yager Stadium article says in use since 1895). Closed 1982; site of the Biological Sciences Building (Pearson Hall).", osm=False)
B("herron_gymnasium", "Herron Gymnasium", "athletics", "main", 1897, "year", 1986, "demolished", "corroborated", [T_N, wp("Herron Gymnasium")],
  "Later Van Voorhis Hall; NRHP 1979. (A 2025 Miami Student article's claim that it was cleared for Ogden Hall conflicts with this.)", other=["Van Voorhis Hall"], osm=False)
B("patterson_place", "Patterson Place", "house", "western", 1898, "year", None, "standing", "sourced", [T_P])

# ---------------- 1900–1941 ----------------
B("mckee_hall", "McKee Hall", "residence", "western", 1904, "year", None, "standing", "sourced", [T_P, "ms_2025_women"], "Named McKee Hall in 1917 (formerly 'New Hall').", other=["New Hall"])
B("huston_house", "Huston House (215 East Spring Street)", "house", "main", 1905, "year", 1963, "moved", "sourced", [WT],
  "Sold to Miami 1947; sold in 1963 and moved several miles west of Oxford. Site now the western part of Hanna House.", osm=False)
B("lutheran_parsonage", "Lutheran Parsonage (221 East Spring Street)", "house", "main", 1896, "approximate", None, "demolished", "sourced", [WT],
  "Built about 1896; removed after Miami acquired it (year not stated).", osm=False)
B("hepburn_hall_first", "Hepburn Hall (first)", "residence", "main", None, "unknown", None, "demolished", "sourced", ["mu_hepburn_hall", wp("King Library (Miami University)")],
  "Miami's first women's residence hall, west and north of Bishop Hall. A fire burned it on 14 Jan 1908 (Wikipedia). Demolished to make way for King Library (name reassigned). Build and demolition years not found.", osm=False)
B("hall_auditorium", "Hall Auditorium", "academic", "main", 1908, "year", None, "standing", "corroborated", [T_N, wp("Hall Auditorium")],
  "Built as the Administration/Auditorium Building; renamed Benton Hall 1926, Hall Auditorium 1969.", other=["Auditorium Building", "Benton Hall (1926–1969)"])
B("tallawanda_apartments", "Tallawanda Apartments (Tallawanda Hall)", "residence", "main", 1908, "year", None, "demolished", "corroborated", [WT, T_NU],
  "Built privately by Miami faculty; Miami purchased it in 1952 as a women's residence hall ('Tallawanda Hall acquired'). Now a parking lot; demolition year not found.", osm=False)
B("mcguffey_hall", "McGuffey Hall", "academic", "main", 1909, "year", None, "standing", "corroborated", [T_N, wp("McGuffey Hall"), "ghmchs_mcguffey"],
  "Built as the Normal College's South Pavilion; wings 1915, 1916 and 1925.", other=["South Pavilion, Normal College"])
B("alumni_hall", "Alumni Hall", "library", "main", 1910, "year", None, "standing", "conflicting", [wp("Alumni Hall (Miami University)"), T_N],
  "Wikipedia: completed 1910; Miami's timeline lists it under 1909 (Carnegie award). Renamed from Alumni Library to Alumni Hall when King Library was completed (1972). Chose 1910: 1909 appears to be the year of the Carnegie award, not completion.", other=["Alumni Library"])
B("joyner_house", "Joyner House", "house", "main", 1910, "year", 2026, "demolished", "corroborated", [WT, "wcpo_2026", "oxfreepress_2025"],
  "Herald House in the 1920s; sold to Miami 1967; demolition began June 2026 (WCPO).", other=["Herald House"])
B("stafford_house", "Stafford House (216 East High Street)", "house", "uptown_edge", 1911, "year", 2008, "demolished", "sourced", [WT], "Acquired by Miami 2007, razed 2008.", osm=False)
B("new_and_south_cottage", "New Cottage and South Cottage", "residence", "main", 1911, "approximate", 1923, "demolished", "estimate", [WT],
  "Temporary women's housing 'for about a dozen years' before Wells Hall was built on the site in 1923.", osm=False)
B("bishop_hall", "Bishop Hall", "residence", "main", 1912, "year", None, "standing", "corroborated", [T_N, wp("Bishop Hall (Miami University)")], "Built as a residence for women (construction began 1911).")
B("sawyer_hall", "Sawyer Hall", "athletics", "western", 1914, "year", None, "standing", "corroborated", [T_P, "ms_2025_women"], other=["Sawyer Gymnasium"])
B("kelley_studio", "Edgar Stillman Kelley Studio", "academic", "western", 1916, "year", None, "standing", "sourced", [T_P], osm="Edgar Stillman-Kelley Studio")
B("clark_gate", "Clark Gate", "landmark", "western", 1916, "year", None, "standing", "sourced", [T_P])
B("kumler_chapel", "Kumler Chapel", "landmark", "western", 1918, "year", None, "standing", "corroborated", [T_P, wp("Kumler Chapel")], "Built 1917–18; architect Thomas Hastings.")
B("western_bridges", "Western Campus bridges", "landmark", "western", 1920, "decade", None, "standing", "estimate", [T_P], "Miami's timeline: '1920s'.", osm=False)
B("ernst_nature_theatre", "Ernst Nature Theatre", "landmark", "western", 1922, "year", None, "standing", "sourced", [T_P], osm=False)
B("wells_hall", "Wells Hall", "residence", "main", 1923, "year", 2026, "demolished", "corroborated", [T_N, WT, "wcpo_2026"],
  "Built 1922–23 as a residence for women; last used 2019; demolition began June 2026 (WCPO).")
B("ogden_hall", "Ogden Hall", "residence", "main", 1924, "year", None, "standing", "corroborated", [T_N, wp("Ogden Hall (Miami University)")],
  "Residence for men and student center; construction began 1923; renovated and extended 1999.", events=[(1999, "renovated and extended")])
B("university_hospital_1924", "University Hospital (1924)", "student_life", "main", 1924, "year", None, "unknown", "sourced", [T_N],
  "Relationship to the later MacMillan Hospital / MacMillan Hall not established.", osm=False)
B("new_freshman_dormitory_1924", "New Freshman Dormitory (1924)", "residence", "main", 1924, "year", None, "unknown", "sourced", [T_N],
  "Listed as 'constructed as residence for men'; later name not established.", osm=False)
B("western_steam_plant", "Western Steam Plant", "utility", "western", 1924, "year", None, "standing", "sourced", [T_P], osm="Steam Plant / Western Maintenance Building")
B("irvin_hall", "Irvin Hall", "academic", "main", 1925, "year", None, "standing", "sourced", [T_N], "Constructed as a recitation building.")
B("fisher_hall_old", "Fisher Hall (former Oxford Female College / Oxford Retreat)", "residence", "main", None, "unknown", 1979, "demolished", "corroborated",
  [T_N, wp("Oxford College for Women"), wp("Wilson Hall (Miami University)")],
  "Oxford Female College (chartered 1854), later the Oxford Retreat sanitarium; purchased by Miami 15 Aug 1925 and renamed; men's residence hall and university theatre; torn down 1979; the Marcum Center (1982) stands on the site. Original build year not found.",
  events=[(1925, "purchased by Miami, renamed Fisher Hall"), (1979, "torn down")], osm=False)
B("mary_lyon_hall", "Mary Lyon Hall", "residence", "western", 1925, "year", 2016, "demolished", "corroborated", [T_P, wp("Mary Lyon Residence Hall")], "Built 1923–25.", osm=False)
B("western_lodge", "Western Lodge", "other", "western", 1926, "year", None, "standing", "sourced", [T_P])
B("wilson_hall", "Wilson Hall", "residence", "main", 1926, "year", 2019, "demolished", "sourced", [wp("Wilson Hall (Miami University)")],
  "Built as 'The Pines', an annex of the Oxford Retreat; sold to Miami 1936; renamed Wilson Hall 1986; demolition approved Feb 2019 for that summer (completion not separately confirmed).",
  other=["The Pines"], osm=False)
B("presser_hall", "Presser Hall", "academic", "western", 1931, "year", None, "standing", "sourced", [T_P], "Listed among the Western College buildings acquired in 1974.")
B("kreger_hall", "Kreger Hall", "academic", "main", 1931, "year", None, "standing", "sourced", [T_N, "mu_news_2014"],
  "Built as the center section of Hughes Hall for chemistry; now Kreger Hall. Reopened 2014 with a new wing for physics.", other=["Hughes Hall (center section)"],
  events=[(2014, "reopened with a new wing")])
B("withrow_court", "Withrow Court", "athletics", "main", 1931, "year", 2016, "demolished", "conflicting", [T_N, wp("Withrow Hall"), "mu_parking_2016", "mu_news_2018"],
  "Miami's timeline: constructed 1931; Wikipedia: inaugurated Feb 1932. Closed 2016. Wikipedia says it was repurposed into Withrow Hall; Miami's 2016–17 parking map ('Withrow Court Demolition') and 2018 news ('built on site of original Withrow Court') indicate demolition and a new building.", osm=False)
B("stancote_house", "Stancote House", "house", "western", 1932, "year", None, "standing", "sourced", [T_P, "ms_2025_women"], "Official residence from 1948.")
B("corson_house", "Corson House", "house", "western", 1930, "decade", None, "standing", "estimate", [T_P], "Miami's timeline: '1930s'.", osm=False)
B("symmes_hall", "Symmes Hall", "residence", "main", 1939, "year", None, "standing", "sourced", [T_N, "mu_news_2015"], "Renovated 2015.")
B("hamilton_hall", "Hamilton Hall", "residence", "main", 1940, "year", None, "standing", "sourced", [T_N], "Built as a residence for women.")
B("richard_hall", "Richard Hall", "residence", "main", 1940, "year", None, "standing", "conflicting", [wp("Richard Hall (Miami University)"), T_N],
  "Wikipedia: built 1940 as 'South Hall'; Miami's timeline: 1941. New wing and renamed Richard Hall 6 Dec 1952. Chose 1940: Wikipedia gives construction details (north unit built 1940, $220,000).", other=["South Hall (1940–1952)"],
  events=[(1952, "new wing; renamed Richard Hall")])
B("beta_campanile", "Beta Theta Pi Campanile (Beta Bells)", "landmark", "main", 1941, "year", None, "standing", "sourced", [T_N], osm=False)
B("vetville", "Vetville", "residence", "main", 1941, "year", None, "demolished", "sourced", [T_NU],
  "Temporary housing; Miami's timeline lists it as 'erected' in 1941. Removal year not found.", osm=False)

# ---------------- 1942–1970 ----------------
B("harris_dining_wooden", "Harris Hall (war-surplus wooden dining hall)", "dining", "main", 1946, "year", None, "demolished", "sourced", [wp("Harris Dining Hall (Miami University)")],
  "Wooden structure purchased as war surplus in 1946 for dining; replaced by Harris Hall (1961). Removal year not found.", osm=False)
B("clawson_hall", "Clawson Hall", "residence", "western", 1946, "year", None, "standing", "conflicting", [T_P, "ms_2025_women"], "Miami's timeline: 1946; The Miami Student: dedicated 1948.")
B("boyd_hall", "Boyd Hall", "academic", "western", 1947, "year", None, "standing", "sourced", [T_P])
B("reid_hall_old", "Reid Hall (original)", "residence", "main", 1949, "year", 2006, "demolished", "conflicting", [T_NU, wp("Reid Hall (Miami University)")],
  "Miami's timeline: 1949; Wikipedia: 1948. Demolished 2006 for the Farmer School of Business; name reused in Heritage Commons.", osm=False)
B("rowan_hall", "Rowan Hall", "academic", "main", 1949, "year", 2014, "incorporated", "corroborated", [T_NU, wp("Rowan Hall"), "oxfreepress_2025"],
  "Naval Science building; repurposed (from 2011) with Culler and Gaskill Halls into the Armstrong Student Center (opened 2014).", osm=False)
B("upham_hall", "Upham Hall", "academic", "main", 1949, "year", None, "standing", "corroborated", [T_NU, wp("Upham Hall (Miami University)")],
  "Centre 1949, north wing 1950, south wing 1965.", events=[(1950, "north wing"), (1965, "south wing")])
B("billings_natatorium", "Billings Natatorium", "athletics", "main", 1952, "year", None, "unknown", "sourced", [T_NU], "Later history not found.", osm=False)
B("collins_hall", "Collins Hall", "residence", "main", 1952, "year", None, "standing", "sourced", [T_NU, "mu_news_2015"], "Renovated 2015.")
B("mcbride_hall", "McBride Hall", "residence", "main", 1952, "year", None, "standing", "sourced", [T_NU, "mu_news_2015"], "Renovated 2015.")
B("east_dining_hall", "East Dining Hall", "dining", "main", 1954, "year", None, "unknown", "sourced", [T_NU], "Later history not found.", osm=False)
B("porter_hall", "Porter Hall", "residence", "main", 1956, "year", None, "standing", "corroborated", [T_NU, "ms_2025_women"], "Dedicated 1957.")
B("roudebush_hall", "Roudebush Hall", "admin", "main", 1956, "year", None, "standing", "sourced", [T_NU],
  "Miami's timeline lists the 'Administration Building' in 1956; identification with Roudebush Hall is unverified.", other=["Administration Building"])
B("shriver_center", "Shriver Center", "student_life", "main", 1957, "year", None, "standing", "sourced", [wp("Shriver Center")],
  "Construction began Oct 1954; completed 1957; opened as the University Center in 1958; renovated 1981.", other=["University Center"])
B("scott_hall", "Scott Hall", "residence", "main", 1957, "year", None, "standing", "corroborated", [T_NU, "mu_news_2018"], "Renovated 2018.")
B("dennison_hall", "Dennison Hall", "residence", "main", 1958, "year", None, "standing", "sourced", [T_NU, "mu_news_2014"], "Work began 1957, completed 1958; absorbed Erickson Dining Hall as living space (2014–15).")
B("hiestand_hall", "Hiestand Hall", "academic", "main", 1958, "year", None, "standing", "sourced", [T_NU])
B("miami_manor", "Miami Manor", "residence", "main", 1958, "year", None, "unknown", "sourced", [T_NU], "Later history not found.", osm=False)
B("sesquicentennial_chapel", "Sesquicentennial Chapel", "landmark", "main", 1959, "year", None, "standing", "corroborated", [T_NU, wp("Sesquicentennial Chapel")])
B("brandon_hall", "Brandon Hall", "residence", "main", 1959, "year", None, "standing", "sourced", [T_NU])
B("mcfarland_hall", "McFarland Hall", "residence", "main", 1959, "year", None, "standing", "sourced", [T_NU, "mu_news_2014"], "Renovated 2013–14.")
B("laws_hall", "Laws Hall", "academic", "main", 1959, "year", None, "standing", "corroborated", [T_NU, wp("Laws Hall (Miami University)")])
B("williams_hall", "Williams Hall", "academic", "main", 1959, "year", 2026, "demolished", "corroborated", [T_NU, wp("Williams Hall (Miami University)"), "wcpo_2026"],
  "Built for communications and WMUB radio; demolished June 2026.")
B("harrison_hall", "Harrison Hall", "academic", "main", 1960, "year", None, "standing", "corroborated", [T_NU, wp("Harrison Hall")], "Built on the site of Old Main.")
B("equestrian_center", "John W. Browne Equestrian Center", "athletics", "outlying", 1960, "year", None, "standing", "sourced", [T_NU, "mu_news_2024"],
  "Stables constructed 1960 (spelled 'Browne' in Miami's timeline); indoor equestrian center opened 2021.", events=[(2021, "indoor equestrian center opened")],
  osm="John W. Brown Equestrian Center")
B("maccracken_hall", "MacCracken Hall", "residence", "main", 1961, "year", None, "standing", "corroborated", [T_NU, wp("MacCracken Hall")], "Work began 1959, completed 1961.")
B("anderson_hall", "Anderson Hall", "residence", "main", 1961, "year", None, "standing", "sourced", [T_NU, "mu_news_2014"], "Renovated 2013–14.")
B("dodds_hall", "Dodds Hall", "residence", "main", 1961, "year", None, "standing", "sourced", [T_NU])
B("stanton_hall", "Stanton Hall", "residence", "main", 1961, "year", None, "standing", "sourced", [T_NU])
B("harris_hall", "Harris Hall", "dining", "main", 1961, "year", None, "standing", "corroborated", [T_NU, wp("Harris Dining Hall (Miami University)")],
  "Dedicated 16 Sep 1961; closed as a dining hall after 2016–17.")
B("erickson_dining_hall", "Erickson Dining Hall", "dining", "main", 1961, "year", 2014, "incorporated", "sourced", [T_NU, "mu_news_2014"], "Converted into living space for Dennison Hall (2014–15).", osm=False)
B("culler_hall", "Culler Hall", "academic", "main", 1961, "year", 2014, "incorporated", "corroborated", [T_NU, wp("Culler Hall (Miami University)"), wp("Rowan Hall")],
  "Dedicated 29 Jan 1961; repurposed into the Armstrong Student Center.", osm=False)
B("gaskill_hall", "Gaskill Hall", "academic", "main", None, "unknown", 2014, "incorporated", "sourced", [wp("Rowan Hall"), "oxfreepress_2025"],
  "Repurposed with Rowan and Culler into the Armstrong Student Center; build year not found.", osm=False)
B("dorsey_hall", "Dorsey Hall", "residence", "main", 1962, "year", None, "standing", "sourced", [T_NU, "mu_news_2015"], "Renovated 2015.")
B("minnich_hall", "Minnich Hall", "residence", "main", 1962, "year", None, "standing", "corroborated", [T_NU, "mu_news_2018"], "Renovated 2018.")
B("warfield_hall", "Warfield Hall", "admin", "main", 1962, "year", None, "standing", "sourced", [T_NU])
B("phillips_hall", "Phillips Hall", "academic", "main", 1962, "year", None, "standing", "sourced", [T_NU])
B("macmillan_hall", "MacMillan Hall", "admin", "main", 1962, "year", None, "standing", "sourced", [T_NU, T_C],
  "MacMillan Hospital center wing constructed 1962; renovated as the Center for American and World Cultures, opened 2003.", other=["MacMillan Hospital"])
B("alexander_dining_hall", "Alexander Dining Hall", "dining", "western", 1962, "year", None, "unknown", "sourced", [T_P], "Later history not found.", osm=False)
B("thomson_hall", "Thomson Hall", "residence", "western", 1963, "year", 2025, "demolished", "sourced", [T_P, "oxfreepress_2025"],
  "Spelled 'Thompson Hall' in Miami's timeline. Demolished January 2025.", osm=False)
B("hanna_house", "Hanna House", "admin", "main", 1963, "year", 2026, "demolished", "corroborated", [WT, "wcpo_2026", "ms_2025_women"],
  "Built 1963 on the sites of two houses, dedicated 1964; demolition began June 2026.")
B("flower_hall", "Flower Hall", "residence", "main", 1966, "year", None, "standing", "corroborated", [T_NU, "ms_2025_women"])
B("hahne_hall", "Hahne Hall", "residence", "main", 1966, "year", None, "standing", "sourced", [T_NU])
B("king_library", "King Library", "library", "main", 1966, "year", None, "standing", "corroborated", [T_NU, T_P, wp("King Library (Miami University)")],
  "South section 1966 (King Undergraduate Library); north section completed 1972; three-phase renovation late 1990s–2007.", events=[(1972, "north section completed")])
B("shideler_hall", "Shideler Hall", "academic", "main", 1967, "year", None, "standing", "sourced", [T_NU, "mu_news_2015"], "Renovated 2015–16.")
B("murstein_alumni_center", "Murstein Alumni Center", "admin", "main", 1967, "year", None, "standing", "sourced", [T_NU])
B("bevier_cottage", "Bevier Cottage", "house", "main", None, "unknown", 1967, "demolished", "sourced", [WT],
  "Frame dwelling sold to Miami 1920, named Bevier Cottage 1959, razed 1967 for the new laboratory school.", osm=False)
B("benton_hall", "Benton Hall", "academic", "main", 1968, "year", None, "standing", "corroborated", [T_NU, wp("Benton Hall (Miami University)")])
B("millett_hall", "Millett Hall", "athletics", "athletics", 1968, "year", None, "standing", "corroborated", [T_NU, wp("Millett Hall"), "oxfreepress_2025"],
  "Opened 2 Dec 1968. A replacement arena on Cook Field is planned.", osm="Millett Assembly Hall")
B("mcguffey_lab_school", "McGuffey Laboratory School", "academic", "main", 1969, "year", None, "unknown", "sourced", [T_NU, WT],
  "Built on the Bevier Cottage site; relationship to today's McGuffey Montessori School unverified.", osm=False)
B("center_performing_arts", "Center for Performing Arts", "academic", "main", 1969, "year", None, "standing", "conflicting", [T_NU, wp("Oxford College for Women")],
  "Miami's timeline: 1969; Wikipedia (Oxford College article): completed 1968.", osm="Center for the Performing Arts")
B("emerson_hall", "Emerson Hall", "residence", "main", 1969, "year", None, "standing", "conflicting", [T_NU, "ms_2025_women"], "Miami's timeline: 1969; The Miami Student: built and dedicated 1970.")
B("morris_hall", "Morris Hall", "residence", "main", 1969, "year", None, "standing", "sourced", [T_NU])
B("tappan_hall", "Tappan Hall", "residence", "main", 1970, "year", None, "standing", "sourced", [T_NU])
B("hughes_laboratories", "Hughes Laboratories", "academic", "main", 1970, "year", None, "standing", "sourced", [T_NU])
B("hoyt_hall", "Hoyt Hall", "library", "western", 1971, "year", None, "standing", "conflicting", [T_P, wp("Hoyt Hall (Miami University)"), wp("Alumnae Hall (Western College for Women)"), "ms_2025_women"],
  "Western College library: Miami's timeline 1971; Wikipedia 1967–71 (and 'opened 1970'); dedicated 1973; renamed Hoyt Hall 1981.", other=["Hoyt Library"])

# ---------------- 1971–present ----------------
B("mckie_field_1973", "McKie Field (1973)", "athletics", "athletics", 1973, "year", None, "unknown", "sourced", [T_P], "Baseball field; relationship to McKie Field at Hayden Park (2002) not established.", osm=False)
B("goggin_ice_arena", "Goggin Ice Arena", "athletics", "athletics", 1976, "year", 2006, "demolished", "conflicting", [wp("Goggin Ice Arena"), T_P, T_C],
  "Wikipedia: opened Sep 1976, demolished Sep 2006. Miami's timeline: constructed 1975, demolished 2003. Named Miami Ice Arena until 1984. Chose 1976/2006: the team used the arena until moving to the new Goggin Ice Center in 2006.", other=["Miami Ice Arena"], osm=False)
B("art_museum", "Miami University Art Museum", "academic", "western", 1978, "year", None, "standing", "sourced", [T_P],
  "Today's Richard and Carole Cocks Art Museum (renaming year not found).", osm="Richard and Carole Cocks Art Museum")
B("bachelor_hall", "Bachelor Hall", "academic", "main", 1979, "year", None, "standing", "conflicting", [T_P, wp("Bachelor Hall (Miami University)")], "Miami's timeline: 1979; Wikipedia: 1978.")
B("marcum_center", "Marcum Conference Center", "other", "main", 1982, "year", None, "standing", "corroborated", [T_P, wp("Oxford College for Women")],
  "Opened Sep 1982 on the site of Fisher Hall; today The Marcum Hotel & Conference Center.", osm="The Marcum Hotel & Conference Center")
B("yager_stadium", "Yager Stadium", "athletics", "athletics", 1983, "year", None, "standing", "corroborated", [T_P, wp("Yager Stadium (Miami University)")], "Opened 1 Oct 1983; renovated 2003–05.", osm=False)
B("havighurst_hall", "Havighurst Hall", "residence", "western", 1983, "year", None, "standing", "sourced", ["ms_2025_women"])
B("art_building", "Art Building", "academic", "main", 1986, "year", None, "standing", "sourced", [T_P])
B("pearson_hall", "Pearson Hall", "academic", "main", 1986, "year", None, "standing", "corroborated", [T_P, wp("Pearson Hall (Miami University)")],
  "Built as the Biological Sciences Building on the site of Miami Field.", other=["Biological Sciences Building"])
B("rec_sports_center", "Recreational Sports Center", "student_life", "athletics", 1994, "year", None, "standing", "sourced", [T_P])
B("health_services_center", "Health Services Center", "student_life", "main", 1996, "year", None, "standing", "sourced", [T_C], osm=False)
B("oxford_water_tower", "Oxford water tower", "utility", "main", None, "unknown", 1998, "demolished", "sourced", [T_C], "Build year not found.", osm=False)
B("pulley_bell_tower", "Verlin Pulley Bell Tower", "landmark", "main", 2001, "year", None, "standing", "sourced", [T_C], osm=False)
B("demske_culinary", "Demske Culinary Support Center", "dining", "outlying", 2001, "year", None, "standing", "sourced", ["mu_dining_dcsc"], "Dining services headquarters 'since 2001'.")
B("child_development_center", "Child Development Center", "other", "western", 2002, "year", None, "standing", "sourced", [T_C])
B("hayden_park", "McKie Field at Hayden Park", "athletics", "athletics", 2002, "year", None, "standing", "sourced", [wp("Hayden Park")], "Opened 24 Mar 2002.", osm="McKie Field Building")
for hid, hname in [("blanchard_house", "Blanchard House"), ("fisher_hall", "Fisher Hall"), ("logan_lodge", "Logan Lodge"), ("pines_lodge", "Pines Lodge"),
                   ("reid_hall", "Reid Hall"), ("tallawanda_hall", "Talawanda Hall"), ("heritage_commons_center", "Heritage Commons Center")]:
    B(hid, hname + (" (Heritage Commons)" if hname in ("Fisher Hall", "Reid Hall", "Talawanda Hall") else ""), "residence", "main", 2005, "year", None, "standing", "sourced",
      [T_C, wp("Reid Hall (Miami University)")],
      "Miami's timeline: Heritage Commons apartments (six buildings) constructed 2005. Assignment of this building to that group is from its name/location in OSM (unverified).",
      osm=hname)
B("goggin_ice_center", "Goggin Ice Center", "athletics", "athletics", 2006, "year", None, "standing", "corroborated", [T_C, wp("Goggin Ice Center")], "Opened 15 Jul 2006.", osm="Goggin Ice Arena")
B("campus_avenue_garage", "Campus Avenue Garage", "utility", "main", 2006, "year", None, "standing", "sourced", [T_C])
B("psychology_building", "Psychology Building", "academic", "main", 2006, "year", None, "standing", "sourced", [T_C])
B("softball_stadium", "Softball Stadium", "athletics", "athletics", 2007, "year", None, "standing", "sourced", [T_C], osm=False)
B("engineering_building", "Engineering Building", "academic", "main", 2007, "year", None, "standing", "sourced", [T_C], "School of Engineering and Applied Science facilities in the new academic quad.")
B("north_parking_garage", "North Parking Garage", "utility", "main", 2008, "year", None, "standing", "sourced", [T_C], osm=False)
B("farmer_school", "Farmer School of Business", "academic", "main", 2009, "year", None, "standing", "sourced", ["mu_news_2024"], "Built on the site of the original Reid Hall.")
B("etheridge_hall", "Etheridge Hall", "residence", "main", 2013, "year", None, "standing", "sourced", ["mu_news_2013"])
B("maplestreet_station", "Maplestreet Station", "dining", "main", 2013, "year", None, "standing", "sourced", ["mu_news_2013"])
B("geothermal_plant", "Geothermal Plant", "utility", "main", 2013, "year", None, "standing", "sourced", ["aia_geothermal"])
B("armstrong_student_center", "Armstrong Student Center", "student_life", "main", 2014, "year", None, "standing", "sourced", ["mu_news_2013", "mu_news_2015"],
  "Opened Jan/Feb 2014, incorporating Rowan, Culler and Gaskill Halls; east wing (phase 2) completed 2017.", events=[(2017, "east wing completed")])
B("hodge_hall", "Hodge Hall", "residence", "western", 2014, "year", None, "standing", "corroborated", ["mu_news_2014", "mu_news_2023_hodge", "ms_2023_hodge"],
  "Opened as Stonebridge Hall; renamed Hodge Hall 2023.", other=["Stonebridge Hall"], events=[(2023, "renamed Hodge Hall")])
B("young_hall", "Young Hall", "residence", "western", 2014, "year", None, "standing", "corroborated", ["mu_news_2014", "mu_young_hall"],
  "Opened as Beechwoods Hall; renamed Young Hall (year not found).", other=["Beechwoods Hall"])
B("hillcrest_hall", "Hillcrest Hall", "residence", "western", 2014, "year", None, "standing", "sourced", ["mu_news_2014"])
B("western_dining_commons", "Western Dining Commons", "dining", "western", 2014, "year", None, "standing", "sourced", ["mu_news_2014"])
B("garden_commons", "Garden Commons", "dining", "main", 2015, "year", None, "standing", "sourced", ["mu_news_2015"])
B("dauch_indoor_sports_center", "David and Anita Dauch Indoor Sports Center", "athletics", "athletics", 2015, "year", None, "standing", "sourced", ["mu_news_2015"], osm=False)
B("chestnut_field_house", "Chestnut Field House", "athletics", "athletics", 2016, "year", None, "standing", "sourced", ["ms_2016_chestnut"], "Opened 31 Jan 2016.")
B("gunlock_apc", "Randy Gunlock Family Athletic Performance Center", "athletics", "athletics", 2017, "year", None, "standing", "sourced", ["jn_apc"], other=["Athletic Performance Center"])
B("marcum_hall", "Marcum Hall", "residence", "main", 2018, "year", None, "standing", "corroborated", ["mu_news_2018", "mu_marcum_hall", "ms_2021_presidents"],
  "Opened 2018 as Presidents Hall; renamed Marcum Hall 2021.", other=["Presidents Hall"], events=[(2021, "renamed Marcum Hall")])
B("withrow_hall", "Withrow Hall", "residence", "main", 2018, "year", None, "standing", "corroborated", ["mu_news_2018", wp("Withrow Hall")], "On the site of Withrow Court (see that entry for the conflict).")
B("nellie_craig_walker_hall", "Nellie Craig Walker Hall", "academic", "main", None, "unknown", None, "standing", "sourced", ["mu_news_2021_ncw", "ms_2025_women"],
  "Formerly the Campus Avenue Building; renamed 2020, dedicated 2021. Build year not found.", other=["Campus Avenue Building"], events=[(2021, "dedicated as Nellie Craig Walker Hall")])
B("clinical_health_sciences", "Clinical Health Sciences and Wellness Building", "academic", "main", 2023, "year", None, "standing", "sourced", ["mu_news_2024"], "Opened June 2023.")
B("mcvey_data_science", "Richard M. McVey Data Science Building", "academic", "main", 2024, "year", None, "standing", "sourced", ["mu_news_2024"], "Opened January 2024.")

# ---------------- Standing buildings with no date found ----------------
for name, kind, note in [
    ("Hepburn Hall", "residence", "Second building with this name (the first was demolished for King Library). Build year not found."),
    ("Garland Hall", "academic", "Shown as part of the Engineering Building complex on the campus map; build year not found."),
    ("Martin Dining Hall", "dining", "A design firm's project page describes it as built in the 1960s (not checked directly)."),
    ("Glos Center", "admin", ""), ("Miami University Police Services Center", "admin", ""), ("Walter L. Gross Center", "athletics", ""),
    ("Cole Service Building", "utility", ""), ("Climer Guest Lodge", "house", ""), ("Advancement Services", "admin", ""),
    ("North Chiller Plant", "utility", ""), ("South Chiller Plant", "utility", ""), ("Recycling Center", "utility", ""),
    ("414 E. Chestnut", "admin", ""), ("Cook Field Storage", "utility", ""), ("East End", "other", ""),
]:
    B(re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_"), name, kind, "main", None, "unknown", None, "standing", "unknown",
      ["mu_hepburn_hall"] if name == "Hepburn Hall" else (["mu_campus_map"] if name == "Garland Hall" else []),
      note or "Build year not found.")

# ---------------- write ----------------
ids = [e["id"] for e in E]
dupes = {i for i in ids if ids.count(i) > 1}
assert not dupes, dupes
src = {**{k: {"title": t, "url": u, "publisher": p} for k, (t, u, p) in SOURCES.items()},
       **{k: {"title": t, "url": u, "publisher": p} for k, (t, u, p) in WIKI.items()}}
used = {s for e in E for s in e["sources"]}
missing = used - set(src)
assert not missing, missing
src = {k: v for k, v in src.items() if k in used}
E.sort(key=lambda e: (e["built_year"] if e["built_year"] is not None else 9999, e["name"]))
doc = {
    "$schema": "./schemas/timeline.schema.json",
    "_comment": "Real Miami University (Oxford) buildings, current and past, compiled from the cited public sources on 2026-09-30. NOTHING here has been checked against University Archives records: every entry is verified:false. confidence: corroborated = 2+ sources agree; sourced = one source; conflicting = sources disagree (see notes); estimate = decade/circa only; unknown = no date found. Scope: university-owned or -used buildings and landmarks (incl. Western College and Oxford College buildings), not fraternity houses or private homes. osm_id links a standing building to its present-day footprint; site is for buildings with no OSM outline (filled in Spike B 3c).",
    "researched": "2026-09-30",
    "sources": src,
    "entries": E,
}
text = json.dumps(doc, indent=2, ensure_ascii=False) + "\n"
target = REPO / "data" / "timeline.json"
if "--write" in sys.argv:
    target.write_text(text, encoding="utf-8")
    print("wrote", target)
else:
    out = pathlib.Path(__file__).with_name("_out") / "timeline.json"
    out.parent.mkdir(exist_ok=True)
    out.write_text(text, encoding="utf-8")
    same = target.exists() and target.read_text(encoding="utf-8") == text
    print("wrote", out, "-", "identical to data/timeline.json" if same else "DIFFERS from data/timeline.json (hand edits?)")
print(len(E), "entries;", len(src), "sources")
from collections import Counter
print(Counter(e["status"] for e in E))
print(Counter(e["confidence"] for e in E))
print("demolished with OSM outline:", [e["name"] for e in E if e["status"] != "standing" and e["osm_id"]])
print("standing without osm_id:", [e["name"] for e in E if e["status"] == "standing" and not e["osm_id"]])
