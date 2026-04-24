INSERT INTO lookups."PointOfInterestStatuses" ("Id", "Name", "PrimaryColor", "SecondaryColor")
VALUES
  (1, 'Open', '#FF6B6B', '#FF8E8E'),
  (2, 'In-progress', '#4D96FF', '#6BAAFF'),
  (3, 'Resolved', '#6BCB77', '#81E38D'),
  (4, 'Permanent', '#9D79BC', '#B18FD1'),
  (5, 'Removed', '#AAAAAA', '#777777')
ON CONFLICT ("Id") DO UPDATE
SET
  "Name" = EXCLUDED."Name",
  "PrimaryColor" = EXCLUDED."PrimaryColor",
  "SecondaryColor" = EXCLUDED."SecondaryColor";