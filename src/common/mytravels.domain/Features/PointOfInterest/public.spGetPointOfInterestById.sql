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
        ROW_NUMBER() OVER (ORDER BY poi."Id") AS "RowId",
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
        poi."PointOfInterestKey"
    FROM public."PointOfInterests" poi
    LEFT JOIN public."PointOfInterestTagAssociations" ita 
        ON ita."PointOfInterestId" = poi."Id"
    LEFT JOIN public."Tags" t 
        ON t."Id" = ita."TagId"
    WHERE poi."PointOfInterestKey" = p_pointOfInterestKey
    ORDER BY poi."DateCreated" DESC;
END;
$$ LANGUAGE plpgsql;