import { FacturaListaTemplateDto } from '@hsm-lib/common/definitions/dtos';

export function FacturaListaTemplate(
  data: FacturaListaTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>
        Estimad@ paciente: <b>{data.patientName}</b>
        <br />
        <br />
        Le saludamos del Hospital Santamaria. Su factura está lista para
        descarga!. Adjunto se encuentra la Factura en la parte inferior del
        mensaje: {data.invoiceNumber}
        <br />
        <br />
        Atentamente,
        <br />
        Servicio al Paciente
        <br />
        Celular 0981764904
      </p>
    </>
  );
}
