CREATE OR REPLACE FUNCTION public.spUpdatePointOfInterestTags(p_pointOfInterestTagType JSON)
RETURNS VOID AS $$
BEGIN
    -- Create temporary table to hold input data
    CREATE TEMP TABLE temp_poi_tags (
        "PointOfInterestId" INTEGER,
        "TagName" VARCHAR(255)
    ) ON COMMIT DROP;

    -- Insert data from JSON parameter into temp table
    INSERT INTO temp_poi_tags ("PointOfInterestId", "TagName")
    SELECT 
        (elem->>'PointOfInterestId')::INTEGER,
        elem->>'TagName'
    FROM json_array_elements(p_pointOfInterestTagType) AS elem;

    -- Insert new tags that don't exist
    INSERT INTO public."Tags" ("Name", "DateCreated")
    SELECT DISTINCT 
        t."TagName", 
        NOW()  -- ✅ returns timestamptz
    FROM temp_poi_tags t
    LEFT JOIN public."Tags" tg ON tg."Name" = t."TagName"
    WHERE tg."Name" IS NULL;

    -- Insert new tag associations that don't exist
    INSERT INTO public."PointOfInterestTagAssociations" ("PointOfInterestId", "TagId", "DateCreated")
    SELECT 
        t."PointOfInterestId",
        tg."Id",
        NOW()  -- ✅ returns timestamptz
    FROM temp_poi_tags t
    INNER JOIN public."Tags" tg ON tg."Name" = t."TagName"
    LEFT JOIN public."PointOfInterestTagAssociations" pta ON 
        pta."PointOfInterestId" = t."PointOfInterestId" AND 
        pta."TagId" = tg."Id"
    WHERE pta."Id" IS NULL;
    
    -- Temp table is automatically dropped at end of transaction
END;
$$ LANGUAGE plpgsql;