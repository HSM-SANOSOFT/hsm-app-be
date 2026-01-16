import { RecetaMedicaTemplateDto } from '@hsm-lib/definitions/dtos';

export function RecetaMedicaTemplate(
  data: RecetaMedicaTemplateDto,
): React.ReactNode {
  return (
    <>
      <>
        <p>
          Estimad@ paciente: <i>{data.patientName}</i>
          <br />
          Le saludamos del Hospital Santamaria.
          <br />
          <br />
          Adjunto se encuentra la Receta Medica solicitada.
          <br />
          <br />
          Atentamente
          <br />
          Farmacia
          <br />
          Celular 2404650 ext-415
        </p>
      </>
    </>
  );
}
