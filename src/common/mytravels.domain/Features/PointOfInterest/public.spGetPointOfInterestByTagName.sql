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
    "Description" TEXT,
    "ImageResized" BOOLEAN,
    "TagId" INTEGER,
    "TagName" VARCHAR(50),
    "PointOfInterestKey" VARCHAR(40)
    -- ✅ "ROW_NUM" removed — not part of output
) AS $$
BEGIN
    -- A point matches when any one of its tags (or its address) matches, but every
    -- tag of a matching point is returned. Filtering and ranking both happen before
    -- the tag join, so the fan-out cannot collapse the result to one tag per point.
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
            ROW_NUMBER() OVER (
                PARTITION BY poi."PointOfInterestKey"
                ORDER BY poi."DateCreated" DESC
            ) AS "ROW_NUM"
        FROM public."PointOfInterests" poi
    ),
    matched AS (
        SELECT p.*
        FROM latest p
        WHERE p."ROW_NUM" = 1
          AND (
              p."FormattedAddress" ILIKE '%' || p_tagName || '%'
              OR EXISTS (
                  SELECT 1
                  FROM public."PointOfInterestTagAssociations" ita
                  INNER JOIN public."Tags" t ON t."Id" = ita."TagId"
                  WHERE ita."PointOfInterestId" = p."Id"
                    AND t."Name" ILIKE '%' || p_tagName || '%'
              )
          )
    )
    SELECT
        -- "RowId" is the EF key of GetPointOfInterestResponse, so it must stay unique
        -- across the whole result set now that a point spans one row per tag.
        ROW_NUMBER() OVER (ORDER BY m."DateCreated", m."Id", t."Id") AS "RowId",
        m."Id" AS "PointOfInterestId",
        m."Container",
        m."OriginalFileName",
        m."GeneratedBlobName",
        m."Latitude",
        m."Longitude",
        m."DateCreated",
        m."DateTaken",
        m."FormattedAddress",
        m."Description",
        m."ImageResized",
        t."Id" AS "TagId",
        t."Name" AS "TagName",
        m."PointOfInterestKey"
    FROM matched m
    LEFT JOIN public."PointOfInterestTagAssociations" ita
        ON ita."PointOfInterestId" = m."Id"
    LEFT JOIN public."Tags" t
        ON t."Id" = ita."TagId"
    ORDER BY m."DateCreated", t."Id";
END;
$$ LANGUAGE plpgsql;
