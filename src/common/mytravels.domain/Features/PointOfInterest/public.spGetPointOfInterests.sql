CREATE OR REPLACE FUNCTION public.spGetPointOfInterest()
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
    "PointOfInterestKey" VARCHAR(40)
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
        T."PointOfInterestKey"
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
            poi."PointOfInterestKey",
            ROW_NUMBER() OVER (
                PARTITION BY poi."PointOfInterestKey" 
                ORDER BY poi."DateCreated" DESC
            ) AS "ROW_NUM"
        FROM public."PointOfInterests" poi
        LEFT JOIN public."PointOfInterestTagAssociations" ita 
            ON ita."PointOfInterestId" = poi."Id"
        LEFT JOIN public."Tags" t 
            ON t."Id" = ita."TagId"
    ) AS T
    WHERE T."ROW_NUM" = 1
    ORDER BY T."DateCreated";
END;
$$ LANGUAGE plpgsql;