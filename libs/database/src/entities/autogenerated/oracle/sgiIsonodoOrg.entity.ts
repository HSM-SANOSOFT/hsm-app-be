import {
Entity,
Column,
PrimaryColumn,
ManyToOne,
OneToOne,
JoinColumn,
Index,
Unique,
} from 'typeorm';



@Entity({ name: 'SGI_ISONODO_ORG', schema: 'SIS' })
export class SgiIsonodoOrgEntity {

  @PrimaryColumn({
  name: 'ISNODOID',
    type: 'number',
    precision: 4,
  })
  isnodoid: number;

  @Column({
  name: 'ISNODODETA',
    type: 'char',
    length: 50,
    nullable: true,
  })
  isnododeta: string | null;



}
