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
    "Description" TEXT,
    "ImageResized" BOOLEAN,
    "TagId" INTEGER,
    "TagName" VARCHAR(50),
    "PointOfInterestKey" VARCHAR(40),
    "CorrelationId" UUID
) AS $$
BEGIN
    -- The newest version of each point is picked BEFORE the tag join. Ranking after
    -- the join would number the duplicated tag rows 1..n and "ROW_NUM" = 1 would
    -- keep a single arbitrary tag per point.
    RETURN QUERY
    WITH latest AS (
        SELECT
            poi."Id",
            poi."Container",
            poi."OriginalFileName",
            poi."GeneratedBlobName",
            poi."Latitude",
            poi."Longitude",
            poi."DateCreated",
            poi."DateTaken",
            poi."FormattedAddress",
            poi."Description",
            poi."ImageResized",
            poi."PointOfInterestKey",
            poi."CorrelationId",
            ROW_NUMBER() OVER (
                PARTITION BY poi."PointOfInterestKey"
                ORDER BY poi."DateCreated" DESC
            ) AS "ROW_NUM"
        FROM public."PointOfInterests" poi
    )
    SELECT
        -- "RowId" is the EF key of GetPointOfInterestResponse, so it must stay unique
        -- across the whole result set now that a point spans one row per tag.
        ROW_NUMBER() OVER (ORDER BY p."DateCreated", p."Id", t."Id") AS "RowId",
        p."Id" AS "PointOfInterestId",
        p."Container",
        p."OriginalFileName",
        p."GeneratedBlobName",
        p."Latitude",
        p."Longitude",
        p."DateCreated",
        p."DateTaken",
        p."FormattedAddress",
        p."Description",
        p."ImageResized",
        t."Id" AS "TagId",
        t."Name" AS "TagName",
        p."PointOfInterestKey",
        p."CorrelationId"
    FROM latest p
    LEFT JOIN public."PointOfInterestTagAssociations" ita
        ON ita."PointOfInterestId" = p."Id"
    LEFT JOIN public."Tags" t
        ON t."Id" = ita."TagId"
    WHERE p."ROW_NUM" = 1
    ORDER BY p."DateCreated", t."Id";
END;
$$ LANGUAGE plpgsql;
