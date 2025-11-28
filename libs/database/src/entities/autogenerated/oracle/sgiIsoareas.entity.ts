import {
Entity,
Column,
ManyToOne,
OneToOne,
JoinColumn,
Index,
Unique,
} from 'typeorm';



@Entity({ name: 'SGI_ISOAREAS', schema: 'SIS' })
export class SgiIsoareasEntity {

  @Column({
  name: 'ISARID',
    type: 'number',
    length: 22,
    precision: 4,
  })
  isarid: number;

  @Column({
  name: 'ISARDETA',
    type: 'char',
    length: 50,
    nullable: true,
  })
  isardeta: string | null;



}
