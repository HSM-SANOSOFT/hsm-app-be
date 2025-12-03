import {
  Entity,
  Column,
  ManyToOne,
  OneToOne,
  JoinColumn,
  Index,
  Unique,
} from 'typeorm';

@Entity({ name: 'SGI_ISODIVISION', schema: 'SIS' })
export class SgiIsodivisionEntity {
  @Column({
    name: 'ISDIRECID',
    type: 'number',
    length: 22,
    precision: 4,
  })
  isdirecid: number;

  @Column({
    name: 'ISDIRECDETA',
    type: 'char',
    length: 50,
    nullable: true,
  })
  isdirecdeta: string | null;
}
