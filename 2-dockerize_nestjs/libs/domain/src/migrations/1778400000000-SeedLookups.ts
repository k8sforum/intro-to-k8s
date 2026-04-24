import { MigrationInterface, QueryRunner } from "typeorm";

export class SeedLookups1778400000000 implements MigrationInterface {
    name = 'SeedLookups1778400000000'

    public async up(queryRunner: QueryRunner): Promise<void> {
        await queryRunner.query(`
            INSERT INTO lookups."PointOfInterestTypes" ("Id", "Name", "PrimaryColor", "SecondaryColor")
            VALUES
              (1, 'Image',    '#FF9F43', '#FFBE76'),
              (2, 'Address',  '#4D96FF', '#6BAAFF'),
              (3, 'Location', '#6BCB77', '#81E38D')
            ON CONFLICT ("Id") DO UPDATE
            SET
              "Name"           = EXCLUDED."Name",
              "PrimaryColor"   = EXCLUDED."PrimaryColor",
              "SecondaryColor" = EXCLUDED."SecondaryColor"
        `);

        await queryRunner.query(`
            INSERT INTO lookups."PointOfInterestStatuses" ("Id", "Name", "PrimaryColor", "SecondaryColor")
            VALUES
              (1, 'Open',        '#FF6B6B', '#FF8E8E'),
              (2, 'In-progress', '#4D96FF', '#6BAAFF'),
              (3, 'Resolved',    '#6BCB77', '#81E38D'),
              (4, 'Permanent',   '#9D79BC', '#B18FD1'),
              (5, 'Removed',     '#AAAAAA', '#777777')
            ON CONFLICT ("Id") DO UPDATE
            SET
              "Name"           = EXCLUDED."Name",
              "PrimaryColor"   = EXCLUDED."PrimaryColor",
              "SecondaryColor" = EXCLUDED."SecondaryColor"
        `);
    }

    public async down(queryRunner: QueryRunner): Promise<void> {
        await queryRunner.query(`DELETE FROM lookups."PointOfInterestStatuses" WHERE "Id" IN (1, 2, 3, 4, 5)`);
        await queryRunner.query(`DELETE FROM lookups."PointOfInterestTypes" WHERE "Id" IN (1, 2, 3)`);
    }
}
