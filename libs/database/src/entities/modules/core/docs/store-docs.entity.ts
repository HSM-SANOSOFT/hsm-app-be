import { DBSchemas } from '@hsm-lib/definitions/enums';
import {
  Column,
  CreateDateColumn,
  DeleteDateColumn,
  Entity,
  JoinColumn,
  ManyToOne,
  PrimaryGeneratedColumn,
  Unique,
  UpdateDateColumn,
} from 'typeorm';

@Entity({ name: 'store_docs', schema: DBSchemas.DOCS })
export class StoreDocsEntity {} 
