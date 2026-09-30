/*
  Love & Honor map pipeline — OpenStreetMap extract for the 4 km map square.
  Also works as-is on https://overpass-turbo.eu (it fills {{bbox}} from the visible map);
  the pipeline replaces {{bbox}} with "south,west,north,east".
  Data © OpenStreetMap contributors, ODbL 1.0.
*/
[out:json][timeout:240];
(
  way["building"]({{bbox}});
  relation["building"]({{bbox}});
  way["highway"]({{bbox}});
  way["railway"]({{bbox}});
  way["waterway"]({{bbox}});
  way["landuse"]({{bbox}});
  relation["landuse"]({{bbox}});
  way["natural"~"^(wood|water|wetland|scrub|grassland)$"]({{bbox}});
  relation["natural"~"^(wood|water|wetland|scrub|grassland)$"]({{bbox}});
  way["leisure"~"^(park|nature_reserve|pitch|golf_course|garden|stadium|track|playground|sports_centre)$"]({{bbox}});
  relation["leisure"~"^(park|nature_reserve|pitch|golf_course|garden|stadium|track|sports_centre)$"]({{bbox}});
  way["boundary"="protected_area"]({{bbox}});
  relation["boundary"="protected_area"]({{bbox}});
  way["amenity"~"^(university|college)$"]({{bbox}});
  relation["amenity"~"^(university|college)$"]({{bbox}});
  relation["boundary"="administrative"]["admin_level"="8"]({{bbox}});
);
out geom;
