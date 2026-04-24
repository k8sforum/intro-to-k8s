CREATE OR REPLACE FUNCTION public.spGetPointOfInterestById(p_pointOfInterestKey VARCHAR(40))
RETURNS TABLE (
    "RowId" BIGINT,
    "PointOfInterestId" INTEGER,
    "Container" VARCHAR(250),
    "OriginalFileName" VARCHAR(250),
    "GeneratedBlobName" VARCHAR(250),
    "Latitude" DOUBLE PRECISION,
    "Longitude" DOUBLE PRECISION,
    "DateCreated" TIMESTAMP WITH TIME ZONE,  -- ✅ corrected
    "FormattedAddress" VARCHAR(300),
    "ImageResized" BOOLEAN,
    "TagId" INTEGER,
    "TagName" VARCHAR(50),
    "TagDateCreated" TIMESTAMP WITH TIME ZONE,
    "PointOfInterestTypeId" INTEGER,
    "PointOfInterestType" VARCHAR(20),
    "PointOfInterestKey" VARCHAR(40),
    "PointOfInterestStatusId" INTEGER,
    "PointOfInterestStatus" VARCHAR(20),
    "PrimaryColor" VARCHAR(30),
    "SecondaryColor" VARCHAR(30)
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        ROW_NUMBER() OVER (ORDER BY poi."Id") AS "RowId",
        poi."Id" AS "PointOfInterestId",
        poi."Container",
        poi."OriginalFileName",
        poi."GeneratedBlobName",
        poi."Latitude",
        poi."Longitude",
        poi."DateCreated",
        poi."FormattedAddress",
        poi."ImageResized",
        t."Id" AS "TagId",
        t."Name" AS "TagName",
        t."DateCreated" AS "TagDateCreated",
        poit."Id" AS "PointOfInterestTypeId",
        poit."Name" AS "PointOfInterestType",
        poi."PointOfInterestKey",
        pois."Id" AS "PointOfInterestStatusId",
        pois."Name" AS "PointOfInterestStatus",
        pois."PrimaryColor",
        pois."SecondaryColor"
    FROM public."PointOfInterests" poi
    INNER JOIN lookups."PointOfInterestTypes" poit 
        ON poit."Id" = poi."PointOfInterestTypeId"
    INNER JOIN lookups."PointOfInterestStatuses" pois 
        ON pois."Id" = poi."PointOfInterestStatusId"
    LEFT JOIN public."PointOfInterestTagAssociations" ita 
        ON ita."PointOfInterestId" = poi."Id"
    LEFT JOIN public."Tags" t 
        ON t."Id" = ita."TagId"
    WHERE poi."PointOfInterestKey" = p_pointOfInterestKey
    ORDER BY poi."DateCreated" DESC;
END;
$$ LANGUAGE plpgsql;