import {
  Entity,
  Column,
  ManyToOne,
  OneToOne,
  JoinColumn,
  Index,
  Unique,
} from 'typeorm';
import { ProvinciasEntity } from './index';

@Entity({ name: 'CANTONES', schema: 'SIS' })
export class CantonesEntity {
  @Column({
    name: 'PRV_CODIGO',
    type: 'varchar',
    length: 2,
  })
  prvCodigo: string;

  @Column({
    name: 'CODIGO',
    type: 'varchar',
    length: 3,
  })
  codigo: string;

  @Column({
    name: 'CANTON',
    type: 'varchar',
    length: 40,
  })
  canton: string;

  @ManyToOne(() => ProvinciasEntity)
  @JoinColumn([{ name: 'PRV_CODIGO', referencedColumnName: 'PRV_CODIGO' }])
  provincias: ProvinciasEntity;
}
