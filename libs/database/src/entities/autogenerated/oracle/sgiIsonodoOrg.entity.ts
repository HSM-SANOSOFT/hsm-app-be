import {
Entity,
Column,
ManyToOne,
OneToOne,
JoinColumn,
Index,
Unique,
} from 'typeorm';



@Entity({ name: 'SGI_ISONODO_ORG', schema: 'SIS' })
export class SgiIsonodoOrgEntity {

  @Column({
  name: 'ISNODOID',
    type: 'number',
    length: 22,
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
