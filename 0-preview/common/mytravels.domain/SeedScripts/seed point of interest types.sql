INSERT INTO lookups."PointOfInterestTypes" ("Id", "Name")
VALUES
  (1, 'Image'),
  (2, 'Address'),
  (3, 'Location')
ON CONFLICT ("Id") DO UPDATE
SET "Name" = EXCLUDED."Name";