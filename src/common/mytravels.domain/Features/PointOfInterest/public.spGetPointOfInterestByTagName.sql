CREATE OR REPLACE FUNCTION public.spGetPointOfInterestByTagName(p_tagName VARCHAR(30))
RETURNS TABLE (
    "RowId" BIGINT,
    "PointOfInterestId" INTEGER,
    "Container" VARCHAR(250),
    "OriginalFileName" VARCHAR(250),
    "GeneratedBlobName" VARCHAR(250),
    "Latitude" DOUBLE PRECISION,
    "Longitude" DOUBLE PRECISION,
    "DateCreated" TIMESTAMP WITH TIME ZONE,  -- ✅ fixed type
    "DateTaken" TIMESTAMP WITH TIME ZONE,
    "FormattedAddress" VARCHAR(300),
    "ImageResized" BOOLEAN,
    "TagId" INTEGER,
    "TagName" VARCHAR(50),
    "PointOfInterestTypeId" INTEGER,
    "PointOfInterestType" VARCHAR(20),
    "PointOfInterestKey" VARCHAR(40),
    "PointOfInterestStatusId" INTEGER,
    "PointOfInterestStatus" VARCHAR(20),
    "PrimaryColor" VARCHAR(30),
    "SecondaryColor" VARCHAR(30)
    -- ✅ "ROW_NUM" removed — not part of output
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        ROW_NUMBER() OVER (ORDER BY T."PointOfInterestId") AS "RowId",
        T."PointOfInterestId",
        T."Container",
        T."OriginalFileName",
        T."GeneratedBlobName",
        T."Latitude",
        T."Longitude",
        T."DateCreated",
        T."DateTaken",
        T."FormattedAddress",
        T."ImageResized",
        T."TagId",
        T."TagName",
        T."PointOfInterestTypeId",
        T."PointOfInterestType",
        T."PointOfInterestKey",
        T."PointOfInterestStatusId",
        T."PointOfInterestStatus",
        T."PrimaryColor",
        T."SecondaryColor"
    FROM (
        SELECT 
            poi."Id" AS "PointOfInterestId",
            poi."Container",
            poi."OriginalFileName",
            poi."GeneratedBlobName",
            poi."Latitude",
            poi."Longitude",
            poi."DateCreated",
            poi."DateTaken",
            poi."FormattedAddress",
            poi."ImageResized",
            t."Id" AS "TagId",
            t."Name" AS "TagName",
            poit."Id" AS "PointOfInterestTypeId",
            poit."Name" AS "PointOfInterestType",
            poi."PointOfInterestKey",
            pois."Id" AS "PointOfInterestStatusId",
            pois."Name" AS "PointOfInterestStatus",
            pois."PrimaryColor",
            pois."SecondaryColor",
            ROW_NUMBER() OVER (
                PARTITION BY poi."PointOfInterestKey" 
                ORDER BY poi."DateCreated" DESC
            ) AS "ROW_NUM"
        FROM public."PointOfInterests" poi
        INNER JOIN lookups."PointOfInterestTypes" poit 
            ON poit."Id" = poi."PointOfInterestTypeId"
        INNER JOIN lookups."PointOfInterestStatuses" pois 
            ON pois."Id" = poi."PointOfInterestStatusId"
        INNER JOIN public."PointOfInterestTagAssociations" ita 
            ON ita."PointOfInterestId" = poi."Id"
        INNER JOIN public."Tags" t 
            ON t."Id" = ita."TagId"
        WHERE pois."Name" != 'Removed'
          AND (
                t."Name" ILIKE '%' || p_tagName || '%'
             OR poi."FormattedAddress" ILIKE '%' || p_tagName || '%'
          )
    ) AS T
    WHERE T."ROW_NUM" = 1
    ORDER BY T."DateCreated";
END;
$$ LANGUAGE plpgsql;