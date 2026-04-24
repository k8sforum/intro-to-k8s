import { MigrationInterface, QueryRunner } from "typeorm";

export class InitialSchema1778329407965 implements MigrationInterface {
    name = 'InitialSchema1778329407965'

    public async up(queryRunner: QueryRunner): Promise<void> {
        await queryRunner.query(`CREATE SCHEMA IF NOT EXISTS "lookups"`);
        await queryRunner.query(`CREATE TABLE "lookups"."PointOfInterestTypes" ("Id" SERIAL NOT NULL, "Name" character varying(20) NOT NULL, "PrimaryColor" character varying(30) NOT NULL, "SecondaryColor" character varying(30) NOT NULL, CONSTRAINT "PK_80122a9a467b1f51266f3f15456" PRIMARY KEY ("Id"))`);
        await queryRunner.query(`CREATE TABLE "lookups"."PointOfInterestStatuses" ("Id" SERIAL NOT NULL, "Name" character varying(20) NOT NULL, "PrimaryColor" character varying(30) NOT NULL, "SecondaryColor" character varying(30) NOT NULL, CONSTRAINT "PK_188132d8f27a03999a5e3af736f" PRIMARY KEY ("Id"))`);
        await queryRunner.query(`CREATE TABLE "Tags" ("Id" SERIAL NOT NULL, "Name" character varying(30) NOT NULL, "DateCreated" TIMESTAMP NOT NULL DEFAULT now(), CONSTRAINT "PK_a7373f792b1d37d5363528a62da" PRIMARY KEY ("Id"))`);
        await queryRunner.query(`CREATE UNIQUE INDEX "IDX_64cee6e6df0da60c0bce595370" ON "Tags" ("Name") `);
        await queryRunner.query(`CREATE TABLE "PointOfInterestTagAssociations" ("Id" SERIAL NOT NULL, "PointOfInterestId" integer NOT NULL, "TagId" integer NOT NULL, "DateCreated" TIMESTAMP NOT NULL DEFAULT now(), CONSTRAINT "PK_e44e847671cd046b9332474f6fb" PRIMARY KEY ("Id"))`);
        await queryRunner.query(`CREATE TABLE "PointOfInterestAuditLogs" ("Id" SERIAL NOT NULL, "QueueName" character varying(100), "Payload" character varying(500), "Sucessful" boolean NOT NULL DEFAULT false, "ErrorMessage" character varying(500), "PointOfInterestId" integer NOT NULL, "DateCreated" TIMESTAMP NOT NULL DEFAULT now(), CONSTRAINT "PK_5efdd82f427d5e32774fe2151dc" PRIMARY KEY ("Id"))`);
        await queryRunner.query(`CREATE TABLE "PointOfInterests" ("Id" SERIAL NOT NULL, "PointOfInterestKey" character varying(40), "Container" character varying(250), "OriginalFileName" character varying(250), "GeneratedBlobName" character varying(250), "Latitude" double precision NOT NULL, "Longitude" double precision NOT NULL, "DateCreated" TIMESTAMP NOT NULL DEFAULT now(), "FormattedAddress" character varying(300), "ImageResized" boolean NOT NULL DEFAULT false, "PointOfInterestTypeId" integer NOT NULL, "PointOfInterestStatusId" integer NOT NULL, "DateUpdated" TIMESTAMP, "UpdatedBy" character varying, "Reason" character varying(500), CONSTRAINT "PK_b315d075ae59a48cd16a9f2a9cb" PRIMARY KEY ("Id"))`);
        await queryRunner.query(`ALTER TABLE "PointOfInterestTagAssociations" ADD CONSTRAINT "FK_a8f62177d6bfa6f2f868e16d454" FOREIGN KEY ("PointOfInterestId") REFERENCES "PointOfInterests"("Id") ON DELETE NO ACTION ON UPDATE NO ACTION`);
        await queryRunner.query(`ALTER TABLE "PointOfInterestTagAssociations" ADD CONSTRAINT "FK_d011b6d4fa7de563e493d9d7629" FOREIGN KEY ("TagId") REFERENCES "Tags"("Id") ON DELETE NO ACTION ON UPDATE NO ACTION`);
        await queryRunner.query(`ALTER TABLE "PointOfInterestAuditLogs" ADD CONSTRAINT "FK_cea4b6307de7e97a37b07b8cb64" FOREIGN KEY ("PointOfInterestId") REFERENCES "PointOfInterests"("Id") ON DELETE NO ACTION ON UPDATE NO ACTION`);
        await queryRunner.query(`ALTER TABLE "PointOfInterests" ADD CONSTRAINT "FK_c7ff58defd43cdda05b9156818d" FOREIGN KEY ("PointOfInterestTypeId") REFERENCES "lookups"."PointOfInterestTypes"("Id") ON DELETE NO ACTION ON UPDATE NO ACTION`);
        await queryRunner.query(`ALTER TABLE "PointOfInterests" ADD CONSTRAINT "FK_dc5ca5668ce261b9e559f01328e" FOREIGN KEY ("PointOfInterestStatusId") REFERENCES "lookups"."PointOfInterestStatuses"("Id") ON DELETE NO ACTION ON UPDATE NO ACTION`);
    }

    public async down(queryRunner: QueryRunner): Promise<void> {
        await queryRunner.query(`ALTER TABLE "PointOfInterests" DROP CONSTRAINT "FK_dc5ca5668ce261b9e559f01328e"`);
        await queryRunner.query(`ALTER TABLE "PointOfInterests" DROP CONSTRAINT "FK_c7ff58defd43cdda05b9156818d"`);
        await queryRunner.query(`ALTER TABLE "PointOfInterestAuditLogs" DROP CONSTRAINT "FK_cea4b6307de7e97a37b07b8cb64"`);
        await queryRunner.query(`ALTER TABLE "PointOfInterestTagAssociations" DROP CONSTRAINT "FK_d011b6d4fa7de563e493d9d7629"`);
        await queryRunner.query(`ALTER TABLE "PointOfInterestTagAssociations" DROP CONSTRAINT "FK_a8f62177d6bfa6f2f868e16d454"`);
        await queryRunner.query(`DROP TABLE "PointOfInterests"`);
        await queryRunner.query(`DROP TABLE "PointOfInterestAuditLogs"`);
        await queryRunner.query(`DROP TABLE "PointOfInterestTagAssociations"`);
        await queryRunner.query(`DROP INDEX "public"."IDX_64cee6e6df0da60c0bce595370"`);
        await queryRunner.query(`DROP TABLE "Tags"`);
        await queryRunner.query(`DROP TABLE "lookups"."PointOfInterestStatuses"`);
        await queryRunner.query(`DROP TABLE "lookups"."PointOfInterestTypes"`);
    }

}
