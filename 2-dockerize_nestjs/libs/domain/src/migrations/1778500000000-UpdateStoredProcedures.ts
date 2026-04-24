import { MigrationInterface, QueryRunner } from "typeorm";
import * as fs from "fs";
import * as path from "path";

export class UpdateStoredProcedures1778500000000 implements MigrationInterface {
    name = 'UpdateStoredProcedures1778500000000'

    public async up(queryRunner: QueryRunner): Promise<void> {
        const featuresDir = path.join(__dirname, '..', 'features');
        const files = this.getSqlFiles(featuresDir);
        for (const file of files) {
            const script = fs.readFileSync(file, 'utf-8').replace(/^﻿/, '');
            await queryRunner.query(script);
        }
    }

    public async down(queryRunner: QueryRunner): Promise<void> {
        throw new Error('Not supported');
    }

    private getSqlFiles(dir: string): string[] {
        const entries = fs.readdirSync(dir, { withFileTypes: true });
        return entries.flatMap((entry) => {
            const fullPath = path.join(dir, entry.name);
            if (entry.isDirectory()) return this.getSqlFiles(fullPath);
            if (entry.isFile() && entry.name.endsWith('.sql')) return [fullPath];
            return [];
        });
    }
}
